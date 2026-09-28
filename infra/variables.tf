variable "subscription_id" {
  description = "Azure subscription ID in which to create the resources."
  type        = string

  validation {
    condition     = can(regex("^[0-9a-fA-F-]{36}$", var.subscription_id))
    error_message = "subscription_id must be a valid GUID."
  }
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "germanywestcentral"
}

variable "environment" {
  description = "Deployment environment name used in tags."
  type        = string
  default     = "dev"
}

variable "workload_name" {
  description = "Workload name used in tags."
  type        = string
  default     = "functionapplogging"
}

variable "resource_group_name" {
  description = "Name of the resource group."
  type        = string
}

variable "managed_identity_name" {
  description = "Name of the user-assigned managed identity."
  type        = string
}

variable "storage_account_name" {
  description = "Globally unique Storage account name."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.storage_account_name))
    error_message = "storage_account_name must contain 3-24 lowercase letters and numbers."
  }
}

variable "deployment_container_name" {
  description = "Blob container used by Flex Consumption One Deploy."
  type        = string
  default     = "deployment"
}

variable "results_container_name" {
  description = "Blob container used for function results."
  type        = string
  default     = "processed-items"
}

variable "work_queue_name" {
  description = "Storage queue used for work items."
  type        = string
  default     = "work-items"
}

variable "log_analytics_workspace_name" {
  description = "Name of the Log Analytics workspace."
  type        = string
}

variable "application_insights_name" {
  description = "Name of the workspace-based Application Insights component."
  type        = string
}

variable "service_plan_name" {
  description = "Name of the FC1 App Service plan."
  type        = string
}

variable "function_app_name" {
  description = "Globally unique Function App name, limited to 32 characters to avoid host ID collisions."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9][A-Za-z0-9-]{0,30}[A-Za-z0-9]$", var.function_app_name))
    error_message = "function_app_name must be 2-32 characters, contain only letters, numbers, and hyphens, and start and end with a letter or number."
  }
}

variable "log_retention_days" {
  description = "Log Analytics retention period in days."
  type        = number
  default     = 30

  validation {
    condition     = var.log_retention_days >= 30 && var.log_retention_days <= 730
    error_message = "log_retention_days must be between 30 and 730."
  }
}

variable "maximum_instance_count" {
  description = "Maximum Flex Consumption instance count."
  type        = number
  default     = 100

  validation {
    condition     = var.maximum_instance_count >= 1 && var.maximum_instance_count <= 1000
    error_message = "maximum_instance_count must be between 1 and 1000."
  }
}

variable "instance_memory_in_mb" {
  description = "Memory allocated to each Flex Consumption instance."
  type        = number
  default     = 2048

  validation {
    condition     = contains([512, 2048, 4096], var.instance_memory_in_mb)
    error_message = "instance_memory_in_mb must be one of 512, 2048, or 4096."
  }
}

variable "grant_deployer_storage_data_roles" {
  description = "Grant the current Terraform principal the minimum Storage data roles needed to create containers and queues."
  type        = bool
  default     = true
}

variable "storage_rbac_propagation_wait" {
  description = "Delay after deployer data-role assignment to allow Azure RBAC propagation."
  type        = string
  default     = "30s"
}

variable "deployment_enabled" {
  description = "Enable the hash-driven local build and One Deploy integration."
  type        = bool
  default     = true
}

variable "deployment_source_path" {
  description = "Path to function source, relative to the workspace root. Used to calculate the deployment hash."
  type        = string
  default     = "src"
}

variable "deployment_test_source_path" {
  description = "Path to test source, relative to the workspace root. Used to calculate the deployment hash."
  type        = string
  default     = "tests"
}

variable "project_path" {
  description = "Function project path passed to Build-FunctionPackage.ps1, relative to the workspace root."
  type        = string
  default     = "src/LoggingDemo.Functions/LoggingDemo.Functions.csproj"
}

variable "test_project_path" {
  description = "Test project path passed to Build-FunctionPackage.ps1, relative to the workspace root."
  type        = string
  default     = "tests/LoggingDemo.Tests/LoggingDemo.Tests.csproj"
}

variable "build_script_path" {
  description = "Path to Build-FunctionPackage.ps1, relative to the workspace root."
  type        = string
  default     = "scripts/Build-FunctionPackage.ps1"
}

variable "deployment_package_path" {
  description = "Package output path passed to Build-FunctionPackage.ps1, relative to the workspace root."
  type        = string
  default     = "artifacts/LoggingDemo.Functions.zip"
}

variable "deployment_timeout_seconds" {
  description = "Azure CLI config-zip timeout in seconds."
  type        = number
  default     = 600

  validation {
    condition     = var.deployment_timeout_seconds >= 60 && var.deployment_timeout_seconds <= 1800
    error_message = "deployment_timeout_seconds must be between 60 and 1800."
  }
}

variable "tags" {
  description = "Additional tags merged with the required workload tags."
  type        = map(string)
  default     = {}
}
