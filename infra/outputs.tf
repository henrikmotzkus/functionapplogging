output "resource_group_name" {
  description = "Name of the resource group."
  value       = azurerm_resource_group.main.name
}

output "function_app_id" {
  description = "Resource ID of the Function App."
  value       = azurerm_function_app_flex_consumption.main.id
}

output "function_app_name" {
  description = "Name of the Function App."
  value       = azurerm_function_app_flex_consumption.main.name
}

output "function_app_default_hostname" {
  description = "Default hostname of the Function App."
  value       = azurerm_function_app_flex_consumption.main.default_hostname
}

output "managed_identity_id" {
  description = "Resource ID of the user-assigned managed identity."
  value       = azurerm_user_assigned_identity.function.id
}

output "managed_identity_client_id" {
  description = "Client ID of the user-assigned managed identity."
  value       = azurerm_user_assigned_identity.function.client_id
}

output "storage_account_id" {
  description = "Resource ID of the keyless Storage account."
  value       = azurerm_storage_account.main.id
}

output "storage_account_name" {
  description = "Name of the keyless Storage account."
  value       = azurerm_storage_account.main.name
}

output "deployment_container_endpoint" {
  description = "Endpoint of the deployment blob container."
  value       = "${azurerm_storage_account.main.primary_blob_endpoint}${azurerm_storage_container.deployment.name}"
}

output "results_container_name" {
  description = "Name of the results blob container."
  value       = azurerm_storage_container.results.name
}

output "work_queue_url" {
  description = "Data-plane URL of the work queue."
  value       = azurerm_storage_queue.work.url
}

output "log_analytics_workspace_id" {
  description = "Resource ID of the Log Analytics workspace."
  value       = azurerm_log_analytics_workspace.main.id
}

output "log_analytics_workspace_name" {
  description = "Name of the Log Analytics workspace."
  value       = azurerm_log_analytics_workspace.main.name
}

output "application_insights_id" {
  description = "Resource ID of the workspace-based Application Insights component."
  value       = azurerm_application_insights.main.id
}

output "logging_workbook_id" {
  description = "Resource ID of the Azure Functions logging demonstration workbook."
  value       = azurerm_application_insights_workbook.logging_demo.id
}
