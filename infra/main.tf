data "azurerm_client_config" "current" {}

locals {
  tags = merge(var.tags, {
    environment = var.environment
    workload    = var.workload_name
    managed-by  = "terraform"
  })
}

resource "azurerm_resource_group" "main" {
  name     = var.resource_group_name
  location = var.location
  tags     = local.tags
}

resource "azurerm_user_assigned_identity" "function" {
  name                = var.managed_identity_name
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.tags
}

resource "azurerm_storage_account" "main" {
  name                              = var.storage_account_name
  resource_group_name               = azurerm_resource_group.main.name
  location                          = azurerm_resource_group.main.location
  account_kind                      = "StorageV2"
  account_tier                      = "Standard"
  account_replication_type          = "ZRS"
  access_tier                       = "Hot"
  https_traffic_only_enabled        = true
  min_tls_version                   = "TLS1_2"
  shared_access_key_enabled         = false
  default_to_oauth_authentication   = true
  allow_nested_items_to_be_public   = false
  cross_tenant_replication_enabled  = false
  infrastructure_encryption_enabled = true
  public_network_access_enabled     = true
  tags                              = local.tags

  blob_properties {
    versioning_enabled = true

    container_delete_retention_policy {
      days = 7
    }

    delete_retention_policy {
      days = 7
    }
  }
}

resource "azurerm_role_assignment" "deployer_blob_data" {
  count = var.grant_deployer_storage_data_roles ? 1 : 0

  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "deployer_queue_data" {
  count = var.grant_deployer_storage_data_roles ? 1 : 0

  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "time_sleep" "storage_rbac_propagation" {
  count = var.grant_deployer_storage_data_roles ? 1 : 0

  create_duration = var.storage_rbac_propagation_wait

  depends_on = [
    azurerm_role_assignment.deployer_blob_data,
    azurerm_role_assignment.deployer_queue_data
  ]
}

resource "azurerm_storage_container" "deployment" {
  name                  = var.deployment_container_name
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"

  depends_on = [time_sleep.storage_rbac_propagation]
}

resource "azurerm_storage_container" "results" {
  name                  = var.results_container_name
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"

  depends_on = [time_sleep.storage_rbac_propagation]
}

resource "azurerm_storage_queue" "work" {
  name               = var.work_queue_name
  storage_account_id = azurerm_storage_account.main.id

  depends_on = [time_sleep.storage_rbac_propagation]
}

resource "azurerm_log_analytics_workspace" "main" {
  name                         = var.log_analytics_workspace_name
  location                     = azurerm_resource_group.main.location
  resource_group_name          = azurerm_resource_group.main.name
  sku                          = "PerGB2018"
  retention_in_days            = var.log_retention_days
  local_authentication_enabled = false
  tags                         = local.tags
}

resource "azurerm_application_insights" "main" {
  name                         = var.application_insights_name
  location                     = azurerm_resource_group.main.location
  resource_group_name          = azurerm_resource_group.main.name
  workspace_id                 = azurerm_log_analytics_workspace.main.id
  application_type             = "web"
  local_authentication_enabled = false
  internet_ingestion_enabled   = true
  internet_query_enabled       = true
  tags                         = local.tags
}

resource "azurerm_service_plan" "function" {
  name                = var.service_plan_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  os_type             = "Linux"
  sku_name            = "FC1"
  tags                = local.tags
}

resource "azurerm_role_assignment" "function_blob_data" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "function_queue_data" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "function_table_data" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "function_monitoring_metrics" {
  scope                = azurerm_application_insights.main.id
  role_definition_name = "Monitoring Metrics Publisher"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
  principal_type       = "ServicePrincipal"
}

resource "time_sleep" "function_rbac_propagation" {
  create_duration = var.storage_rbac_propagation_wait

  depends_on = [
    azurerm_role_assignment.function_blob_data,
    azurerm_role_assignment.function_queue_data,
    azurerm_role_assignment.function_table_data,
    azurerm_role_assignment.function_monitoring_metrics
  ]
}

resource "azurerm_function_app_flex_consumption" "main" {
  name                = var.function_app_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  service_plan_id     = azurerm_service_plan.function.id

  storage_container_type                         = "blobContainer"
  storage_container_endpoint                     = "${azurerm_storage_account.main.primary_blob_endpoint}${azurerm_storage_container.deployment.name}"
  storage_authentication_type                    = "UserAssignedIdentity"
  storage_user_assigned_identity_id              = azurerm_user_assigned_identity.function.id
  runtime_name                                   = "dotnet-isolated"
  runtime_version                                = "10.0"
  maximum_instance_count                         = var.maximum_instance_count
  instance_memory_in_mb                          = var.instance_memory_in_mb
  https_only                                     = true
  public_network_access_enabled                  = true
  webdeploy_publish_basic_authentication_enabled = false
  tags = merge(local.tags, {
    "hidden-link: /app-insights-resource-id" = lower(azurerm_application_insights.main.id)
  })

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.function.id]
  }

  app_settings = {
    # AzureRM currently injects a key-based AzureWebJobsStorage value even when
    # shared keys are disabled. An explicit empty value lets Functions resolve
    # the identity-based AzureWebJobsStorage__* settings below instead.
    "AzureWebJobsStorage"                       = ""
    "AzureWebJobsStorage__accountName"          = azurerm_storage_account.main.name
    "AzureWebJobsStorage__credential"           = "managedidentity"
    "AzureWebJobsStorage__clientId"             = azurerm_user_assigned_identity.function.client_id
    "AzureWebJobsStorage__blobServiceUri"       = azurerm_storage_account.main.primary_blob_endpoint
    "AzureWebJobsStorage__queueServiceUri"      = azurerm_storage_account.main.primary_queue_endpoint
    "AzureWebJobsStorage__tableServiceUri"      = azurerm_storage_account.main.primary_table_endpoint
    "DemoStorage__accountName"                  = azurerm_storage_account.main.name
    "DemoStorage__credential"                   = "managedidentity"
    "DemoStorage__clientId"                     = azurerm_user_assigned_identity.function.client_id
    "DemoStorage__blobServiceUri"               = azurerm_storage_account.main.primary_blob_endpoint
    "DemoStorage__queueServiceUri"              = azurerm_storage_account.main.primary_queue_endpoint
    "DemoResultsContainerName"                  = azurerm_storage_container.results.name
    "DemoWorkQueueName"                         = azurerm_storage_queue.work.name
    "APPLICATIONINSIGHTS_AUTHENTICATION_STRING" = "Authorization=AAD;ClientId=${azurerm_user_assigned_identity.function.client_id}"
  }

  site_config {
    application_insights_connection_string = azurerm_application_insights.main.connection_string
    http2_enabled                          = true
    minimum_tls_version                    = "1.2"
    scm_minimum_tls_version                = "1.2"
  }

  lifecycle {
    ignore_changes = [
      app_settings["AzureWebJobsStorage"]
    ]
  }

  depends_on = [
    time_sleep.function_rbac_propagation
  ]
}
