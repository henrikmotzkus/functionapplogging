resource "azurerm_monitor_diagnostic_setting" "function" {
  name                       = "send-to-log-analytics"
  target_resource_id         = azurerm_function_app_flex_consumption.main.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  enabled_log {
    category = "FunctionAppLogs"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}

resource "azurerm_monitor_diagnostic_setting" "service_plan" {
  name                       = "send-to-log-analytics"
  target_resource_id         = azurerm_service_plan.function.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  enabled_metric {
    category = "AllMetrics"
  }
}

resource "azurerm_monitor_diagnostic_setting" "storage" {
  name                       = "send-to-log-analytics"
  target_resource_id         = azurerm_storage_account.main.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  enabled_metric {
    category = "Transaction"
  }
}

resource "azurerm_application_insights_workbook" "logging_demo" {
  name                = uuidv5("url", azurerm_application_insights.main.id)
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  display_name        = "Azure Functions Logging Layers"
  source_id           = lower(azurerm_application_insights.main.id)
  category            = "workbook"
  tags                = local.tags

  data_json = jsonencode({
    version = "Notebook/1.0"
    items = [
      {
        type = 1
        name = "introduction"
        content = {
          json = "# Azure Functions Logging Layers\nUse `CorrelationId`, `DemoLayer`, logger category, and telemetry type to distinguish application, worker, host, dependency, and platform signals."
        }
      },
      {
        type = 3
        name = "layer-summary"
        content = {
          version      = "KqlItem/1.0"
          title        = "Application traces by layer and category"
          queryType    = 0
          resourceType = "microsoft.insights/components"
          size         = 0
          timeContext = {
            durationMs = 86400000
          }
          query = <<-KQL
            traces
            | extend Category = tostring(customDimensions["CategoryName"]),
                     DemoLayer = tostring(customDimensions["DemoLayer"])
            | summarize Events=count() by DemoLayer, Category, severityLevel
            | order by Events desc
          KQL
        }
      },
      {
        type = 3
        name = "failures"
        content = {
          version      = "KqlItem/1.0"
          title        = "Exceptions and failed dependencies"
          queryType    = 0
          resourceType = "microsoft.insights/components"
          size         = 0
          timeContext = {
            durationMs = 86400000
          }
          query = <<-KQL
            union isfuzzy=true
              (exceptions
                | extend TelemetryType="Exception", Detail=outerMessage, Outcome=problemId),
              (dependencies
                | where success == false
                | extend TelemetryType="Dependency", Detail=strcat(name, " ", data), Outcome=resultCode)
            | project timestamp, TelemetryType, Outcome, Detail, operation_Id
            | order by timestamp desc
          KQL
        }
      },
      {
        type = 3
        name = "retries"
        content = {
          version      = "KqlItem/1.0"
          title        = "Queue retries and poison scenario"
          queryType    = 0
          resourceType = "microsoft.insights/components"
          size         = 0
          timeContext = {
            durationMs = 86400000
          }
          query = <<-KQL
            traces
            | extend Scenario=tostring(customDimensions["Scenario"]),
                     DequeueCount=toint(customDimensions["DequeueCount"]),
                     MessageId=tostring(customDimensions["MessageId"])
            | where Scenario in ("Transient", "Poison")
            | project timestamp, Scenario, DequeueCount, MessageId, message, operation_Id
            | order by timestamp asc
          KQL
        }
      }
    ]
    fallbackResourceIds = [azurerm_application_insights.main.id]
  })
}
