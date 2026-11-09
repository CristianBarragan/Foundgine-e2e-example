output "public_url" {
  value       = local.public_base_url
  description = "CRM web application URL."
}
output "cluster_name" { value = aws_ecs_cluster.main.name }
output "migration_task_definition" { value = aws_ecs_task_definition.migrations.arn }
output "public_subnet_ids" { value = [for subnet in aws_subnet.public : subnet.id] }
output "task_security_group_id" { value = aws_security_group.tasks.id }
output "api_ecr_repository" { value = aws_ecr_repository.api.repository_url }
output "web_ecr_repository" { value = aws_ecr_repository.web.repository_url }
output "migrations_ecr_repository" { value = aws_ecr_repository.migrations.repository_url }
output "keycloak_ecr_repository" { value = aws_ecr_repository.keycloak.repository_url }
output "database_secret_arn" { value = aws_secretsmanager_secret.db.arn }
output "keycloak_admin_username" { value = var.keycloak_admin_username }
output "db_instance_identifier" { value = aws_db_instance.main.identifier }
