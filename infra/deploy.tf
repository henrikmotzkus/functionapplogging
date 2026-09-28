locals {
  workspace_root_absolute_path     = abspath("${path.root}/..")
  deployment_source_absolute_path  = abspath("${local.workspace_root_absolute_path}/${var.deployment_source_path}")
  deployment_tests_absolute_path   = abspath("${local.workspace_root_absolute_path}/${var.deployment_test_source_path}")
  project_absolute_path            = abspath("${local.workspace_root_absolute_path}/${var.project_path}")
  test_project_absolute_path       = abspath("${local.workspace_root_absolute_path}/${var.test_project_path}")
  build_script_absolute_path       = abspath("${local.workspace_root_absolute_path}/${var.build_script_path}")
  deployment_package_absolute_path = abspath("${local.workspace_root_absolute_path}/${var.deployment_package_path}")

  deployment_source_files = var.deployment_enabled ? sort([
    for file in fileset(local.deployment_source_absolute_path, "**") : file
    if length(regexall("(^|/)(bin|obj)(/|$)", file)) == 0
  ]) : []
  deployment_test_files = var.deployment_enabled ? sort([
    for file in fileset(local.deployment_tests_absolute_path, "**") : file
    if length(regexall("(^|/)(bin|obj)(/|$)", file)) == 0
  ]) : []
  deployment_source_hash = var.deployment_enabled ? sha256(join("", concat(
    [
      for file in local.deployment_source_files :
      "src/${file}:${filesha256("${local.deployment_source_absolute_path}/${file}")}"
    ],
    [
      for file in local.deployment_test_files :
      "tests/${file}:${filesha256("${local.deployment_tests_absolute_path}/${file}")}"
    ],
    [try(filesha256(local.build_script_absolute_path), "missing-build-script")]
  ))) : "deployment-disabled"
}

resource "terraform_data" "one_deploy" {
  count = var.deployment_enabled ? 1 : 0

  triggers_replace = [
    local.deployment_source_hash,
    azurerm_function_app_flex_consumption.main.id
  ]

  lifecycle {
    precondition {
      condition     = fileexists(local.build_script_absolute_path)
      error_message = "deployment_enabled is true, but Build-FunctionPackage.ps1 was not found at ${local.build_script_absolute_path}."
    }

    precondition {
      condition     = length(local.deployment_source_files) > 0
      error_message = "deployment_enabled is true, but no source files were found under ${local.deployment_source_absolute_path}."
    }

    precondition {
      condition     = fileexists(local.project_absolute_path)
      error_message = "deployment_enabled is true, but the function project was not found at ${local.project_absolute_path}."
    }

    precondition {
      condition     = fileexists(local.test_project_absolute_path)
      error_message = "deployment_enabled is true, but the test project was not found at ${local.test_project_absolute_path}."
    }
  }

  provisioner "local-exec" {
    interpreter = [
      "pwsh",
      "-NoProfile",
      "-NonInteractive",
      "-ExecutionPolicy",
      "Bypass",
      "-Command"
    ]

    environment = {
      BUILD_SCRIPT_PATH        = local.build_script_absolute_path
      PROJECT_PATH             = local.project_absolute_path
      TEST_PROJECT_PATH        = local.test_project_absolute_path
      PACKAGE_PATH             = local.deployment_package_absolute_path
      FUNCTION_APP_NAME        = azurerm_function_app_flex_consumption.main.name
      FUNCTION_APP_RESOURCE_ID = azurerm_function_app_flex_consumption.main.id
      RESOURCE_GROUP_NAME      = azurerm_resource_group.main.name
      DEPLOYMENT_TIMEOUT       = tostring(var.deployment_timeout_seconds)
    }

    command = <<-POWERSHELL
      $ErrorActionPreference = 'Stop'

      if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw 'Azure CLI was not found on PATH. Install it and run az login before enabling deployment.'
      }

      if (-not (Test-Path -LiteralPath $env:BUILD_SCRIPT_PATH -PathType Leaf)) {
        throw "Build script not found: $env:BUILD_SCRIPT_PATH"
      }

      $packageDirectory = Split-Path -Parent $env:PACKAGE_PATH
      New-Item -ItemType Directory -Path $packageDirectory -Force -ErrorAction Stop | Out-Null

      & pwsh `
        -NoProfile `
        -NonInteractive `
        -ExecutionPolicy Bypass `
        -File $env:BUILD_SCRIPT_PATH `
        -ProjectPath $env:PROJECT_PATH `
        -TestProjectPath $env:TEST_PROJECT_PATH `
        -OutputPath $env:PACKAGE_PATH

      if ($LASTEXITCODE -ne 0) {
        throw "Build-FunctionPackage.ps1 failed with exit code $LASTEXITCODE."
      }

      if (-not (Test-Path -LiteralPath $env:PACKAGE_PATH -PathType Leaf)) {
        throw "Build script completed without creating the expected package: $env:PACKAGE_PATH"
      }

      & az functionapp deployment source config-zip `
        --resource-group $env:RESOURCE_GROUP_NAME `
        --name $env:FUNCTION_APP_NAME `
        --src $env:PACKAGE_PATH `
        --timeout $env:DEPLOYMENT_TIMEOUT `
        --only-show-errors

      if ($LASTEXITCODE -ne 0) {
        $deploymentJson = & az rest `
          --method get `
          --url "https://management.azure.com$($env:FUNCTION_APP_RESOURCE_ID)/deployments?api-version=2024-04-01" `
          --only-show-errors

        $deployment = $null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($deploymentJson)) {
          $deployment = ($deploymentJson | ConvertFrom-Json).value |
            Sort-Object { [DateTimeOffset]$_.properties.received_time } -Descending |
            Select-Object -First 1
        }

        if ($null -ne $deployment) {
          throw "Azure CLI One Deploy/config-zip failed. Latest deployment status: $($deployment.properties.status_text)"
        }

        throw "Azure CLI One Deploy/config-zip failed with exit code $LASTEXITCODE and no deployment status was available."
      }
    POWERSHELL
  }

  depends_on = [
    azurerm_function_app_flex_consumption.main,
    azurerm_monitor_diagnostic_setting.function
  ]
}
