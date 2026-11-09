variable "aws_region" {
  type        = string
  default     = "ap-southeast-2"
  description = "AWS region; Sydney is a sensible default for New Zealand deployments."
}
variable "project_name" {
  type = string
  default = "foundgine-crm"
}
variable "environment" {
  type = string
  default = "production"
}
variable "vpc_cidr" {
  type = string
  default = "10.40.0.0/16"
}
variable "db_instance_class" {
  type = string
  default = "db.t4g.micro"
}
variable "db_allocated_storage" {
  type = number
  default = 30
}
variable "api_cpu" {
  type = number
  default = 512
}
variable "api_memory" {
  type = number
  default = 1024
}
variable "web_cpu" {
  type = number
  default = 256
}
variable "web_memory" {
  type = number
  default = 512
}
variable "keycloak_cpu" {
  type = number
  default = 512
}
variable "keycloak_memory" {
  type = number
  default = 1024
}
variable "api_image_tag" {
  type = string
  default = "latest"
}
variable "web_image_tag" {
  type = string
  default = "latest"
}
variable "migrations_image_tag" {
  type = string
  default = "latest"
}
variable "certificate_arn" {
  type        = string
  default     = ""
  description = "Optional ACM certificate ARN. Without it the ALB is HTTP-only and suitable only for a short-lived demo."
}
variable "domain_name" {
  type        = string
  default     = ""
  description = "Optional public DNS hostname covered by certificate_arn."
}
variable "keycloak_admin_username" {
  type = string
  default = "kc-admin"
}

variable "hosted_zone_id" {
  type        = string
  default     = ""
  description = "Optional Route 53 hosted zone ID for creating the CRM DNS alias. The domain must be covered by the ACM certificate."
}
