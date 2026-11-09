data "aws_availability_zones" "available" {
  state = "available"
}

resource "terraform_data" "configuration_guard" {
  input = {
    domain_name    = var.domain_name
    certificate_arn = var.certificate_arn
    hosted_zone_id = var.hosted_zone_id
  }
  lifecycle {
    precondition {
      condition     = var.domain_name == "" || var.certificate_arn != ""
      error_message = "Set certificate_arn whenever domain_name is configured; otherwise Keycloak is configured for HTTPS but the ALB cannot terminate TLS."
    }
    precondition {
      condition     = var.hosted_zone_id == "" || var.domain_name != ""
      error_message = "hosted_zone_id requires domain_name."
    }
  }
}

locals {
  name            = "${var.project_name}-${var.environment}"
  public_base_url = var.domain_name != "" ? "https://${var.domain_name}" : "http://${aws_lb.app.dns_name}"
  realm_url       = "${local.public_base_url}/auth/realms/foundgine-crm"
  tags            = { Project = var.project_name, Environment = var.environment, ManagedBy = "Terraform" }
  public_cidrs    = { a = "10.40.1.0/24", b = "10.40.2.0/24" }
  private_cidrs   = { a = "10.40.11.0/24", b = "10.40.12.0/24" }
}

resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true
  tags                 = local.tags
}
resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id
  tags   = local.tags
}
resource "aws_subnet" "public" {
  for_each                = local.public_cidrs
  vpc_id                  = aws_vpc.main.id
  cidr_block              = each.value
  availability_zone       = data.aws_availability_zones.available.names[each.key == "a" ? 0 : 1]
  map_public_ip_on_launch = true
  tags                    = merge(local.tags, { Name = "${local.name}-public-${each.key}" })
}
resource "aws_subnet" "private" {
  for_each          = local.private_cidrs
  vpc_id            = aws_vpc.main.id
  cidr_block        = each.value
  availability_zone = data.aws_availability_zones.available.names[each.key == "a" ? 0 : 1]
  tags              = merge(local.tags, { Name = "${local.name}-private-${each.key}" })
}
resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id
  tags   = local.tags
}
resource "aws_route" "internet" {
  route_table_id         = aws_route_table.public.id
  destination_cidr_block = "0.0.0.0/0"
  gateway_id             = aws_internet_gateway.main.id
}
resource "aws_route_table_association" "public" {
  for_each       = aws_subnet.public
  subnet_id      = each.value.id
  route_table_id = aws_route_table.public.id
}
resource "aws_route_table" "private" {
  vpc_id = aws_vpc.main.id
  tags   = local.tags
}
resource "aws_route_table_association" "private" {
  for_each       = aws_subnet.private
  subnet_id      = each.value.id
  route_table_id = aws_route_table.private.id
}

resource "aws_security_group" "alb" {
  name        = "${local.name}-alb"
  description = "Public load balancer ingress"
  vpc_id      = aws_vpc.main.id
  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }
  dynamic "ingress" {
    for_each = var.certificate_arn != "" ? [1] : []
    content {
      from_port   = 443
      to_port     = 443
      protocol    = "tcp"
      cidr_blocks = ["0.0.0.0/0"]
    }
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
  tags = local.tags
}
resource "aws_security_group" "tasks" {
  name        = "${local.name}-tasks"
  description = "Application tasks reachable only through ALB"
  vpc_id      = aws_vpc.main.id
  ingress {
    from_port       = 8080
    to_port         = 8080
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
  tags = local.tags
}
resource "aws_security_group" "web_tasks" {
  name        = "${local.name}-web-tasks"
  description = "Frontend tasks reachable only through ALB; no database ingress"
  vpc_id      = aws_vpc.main.id
  ingress {
    from_port       = 80
    to_port         = 80
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
  tags = local.tags
}
resource "aws_security_group" "db" {
  name        = "${local.name}-db"
  description = "PostgreSQL reachable only from ECS tasks"
  vpc_id      = aws_vpc.main.id
  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.tasks.id]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
  tags = local.tags
}

resource "aws_db_subnet_group" "main" {
  name       = local.name
  subnet_ids = [for subnet in aws_subnet.private : subnet.id]
  tags       = local.tags
}
resource "random_password" "db" {
  length  = 28
  special = false
}
resource "random_password" "keycloak_admin" {
  length  = 28
  special = false
}
resource "aws_secretsmanager_secret" "db" {
  name                    = "${local.name}/database-credentials"
  recovery_window_in_days = 7
  tags                    = local.tags
}
resource "aws_secretsmanager_secret_version" "db" {
  secret_id     = aws_secretsmanager_secret.db.id
  secret_string = jsonencode({ username = "crmadmin", password = random_password.db.result, keycloak_admin_password = random_password.keycloak_admin.result })
}
resource "aws_db_instance" "main" {
  identifier                  = local.name
  engine                      = "postgres"
  instance_class              = var.db_instance_class
  allocated_storage           = var.db_allocated_storage
  storage_type                = "gp3"
  db_name                     = "crm"
  username                    = "crmadmin"
  password                    = random_password.db.result
  db_subnet_group_name        = aws_db_subnet_group.main.name
  vpc_security_group_ids      = [aws_security_group.db.id]
  publicly_accessible         = false
  multi_az                    = false
  storage_encrypted           = true
  deletion_protection         = true
  backup_retention_period     = 7
  auto_minor_version_upgrade = true
  skip_final_snapshot         = false
  final_snapshot_identifier   = "${local.name}-final"
  tags                        = local.tags
}
resource "aws_secretsmanager_secret" "connection" {
  name                    = "${local.name}/connection-string"
  recovery_window_in_days = 7
  tags                    = local.tags
}
resource "aws_secretsmanager_secret_version" "connection" {
  secret_id = aws_secretsmanager_secret.connection.id
  secret_string = "Host=${aws_db_instance.main.address};Port=5432;Database=crm;Username=crmadmin;Password=${random_password.db.result};SSL Mode=Require;Trust Server Certificate=true"
}

resource "aws_ecr_repository" "api" {
  name                 = "${local.name}/api"
  image_tag_mutability = "MUTABLE"
  image_scanning_configuration { scan_on_push = true }
  force_delete = true
  tags = local.tags
}
resource "aws_ecr_repository" "web" {
  name                 = "${local.name}/web"
  image_tag_mutability = "MUTABLE"
  image_scanning_configuration { scan_on_push = true }
  force_delete = true
  tags = local.tags
}
resource "aws_ecr_repository" "migrations" {
  name                 = "${local.name}/migrations"
  image_tag_mutability = "MUTABLE"
  image_scanning_configuration { scan_on_push = true }
  force_delete = true
  tags = local.tags
}
resource "aws_ecr_repository" "keycloak" {
  name                 = "${local.name}/keycloak"
  image_tag_mutability = "MUTABLE"
  image_scanning_configuration { scan_on_push = true }
  force_delete = true
  tags = local.tags
}
resource "aws_ecs_cluster" "main" {
  name = local.name
  setting {
    name  = "containerInsights"
    value = "enabled"
  }
  tags = local.tags
}
resource "aws_cloudwatch_log_group" "api" {
  name = "/ecs/${local.name}/api"
  retention_in_days = 30
  tags = local.tags
}
resource "aws_cloudwatch_log_group" "web" {
  name = "/ecs/${local.name}/web"
  retention_in_days = 30
  tags = local.tags
}
resource "aws_cloudwatch_log_group" "keycloak" {
  name = "/ecs/${local.name}/keycloak"
  retention_in_days = 30
  tags = local.tags
}
resource "aws_cloudwatch_log_group" "migrations" {
  name = "/ecs/${local.name}/migrations"
  retention_in_days = 30
  tags = local.tags
}
resource "aws_iam_role" "execution" {
  name = "${local.name}-ecs-execution"
  assume_role_policy = jsonencode({ Version = "2012-10-17", Statement = [{ Effect = "Allow", Principal = { Service = "ecs-tasks.amazonaws.com" }, Action = "sts:AssumeRole" }] })
  tags = local.tags
}
resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}
resource "aws_iam_role_policy" "secrets" {
  role = aws_iam_role.execution.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [{ Effect = "Allow", Action = ["secretsmanager:GetSecretValue"], Resource = [aws_secretsmanager_secret.connection.arn, aws_secretsmanager_secret.db.arn] }] })
}

resource "aws_lb" "app" {
  name               = substr(local.name, 0, 30)
  load_balancer_type = "application"
  internal           = false
  security_groups    = [aws_security_group.alb.id]
  subnets            = [for subnet in aws_subnet.public : subnet.id]
  tags               = local.tags
}
resource "aws_route53_record" "app" {
  count   = var.domain_name != "" && var.hosted_zone_id != "" ? 1 : 0
  zone_id = var.hosted_zone_id
  name    = var.domain_name
  type    = "A"
  alias {
    name                   = aws_lb.app.dns_name
    zone_id                = aws_lb.app.zone_id
    evaluate_target_health = true
  }
}

resource "aws_lb_target_group" "api" {
  name        = substr("${local.name}-api", 0, 32)
  port        = 8080
  protocol    = "HTTP"
  vpc_id      = aws_vpc.main.id
  target_type = "ip"
  health_check {
    path = "/health"
    matcher = "200"
  }
  tags = local.tags
}
resource "aws_lb_target_group" "web" {
  name        = substr("${local.name}-web", 0, 32)
  port        = 80
  protocol    = "HTTP"
  vpc_id      = aws_vpc.main.id
  target_type = "ip"
  health_check {
    path = "/"
    matcher = "200-399"
  }
  tags = local.tags
}
resource "aws_lb_target_group" "keycloak" {
  name        = substr("${local.name}-auth", 0, 32)
  port        = 8080
  protocol    = "HTTP"
  vpc_id      = aws_vpc.main.id
  target_type = "ip"
  health_check {
    path = "/auth/"
    matcher = "200-399"
  }
  tags = local.tags
}
resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.app.arn
  port              = 80
  protocol          = "HTTP"
  default_action {
    type             = var.certificate_arn != "" ? "redirect" : "forward"
    target_group_arn = var.certificate_arn == "" ? aws_lb_target_group.web.arn : null
    dynamic "redirect" {
      for_each = var.certificate_arn != "" ? [1] : []
      content {
        port = "443"
        protocol = "HTTPS"
        status_code = "HTTP_301"
      }
    }
  }
}
resource "aws_lb_listener" "https" {
  count             = var.certificate_arn != "" ? 1 : 0
  load_balancer_arn = aws_lb.app.arn
  port              = 443
  protocol          = "HTTPS"
  certificate_arn   = var.certificate_arn
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.web.arn
  }
}
resource "aws_lb_listener_rule" "api" {
  listener_arn = var.certificate_arn != "" ? aws_lb_listener.https[0].arn : aws_lb_listener.http.arn
  priority     = 10
  action {
    type = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }
  condition {
    path_pattern { values = ["/api/*", "/mcp*", "/graphql", "/health"] }
  }
}
resource "aws_lb_listener_rule" "keycloak" {
  listener_arn = var.certificate_arn != "" ? aws_lb_listener.https[0].arn : aws_lb_listener.http.arn
  priority     = 20
  action {
    type = "forward"
    target_group_arn = aws_lb_target_group.keycloak.arn
  }
  condition {
    path_pattern { values = ["/auth/*"] }
  }
}

locals {
  api_image       = "${aws_ecr_repository.api.repository_url}:${var.api_image_tag}"
  web_image       = "${aws_ecr_repository.web.repository_url}:${var.web_image_tag}"
  migrations_image = "${aws_ecr_repository.migrations.repository_url}:${var.migrations_image_tag}"
  keycloak_image  = "${aws_ecr_repository.keycloak.repository_url}:latest"
  log_base        = { "awslogs-region" = var.aws_region, "awslogs-stream-prefix" = "ecs" }
}
resource "aws_ecs_task_definition" "api" {
  family                   = "${local.name}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = tostring(var.api_cpu)
  memory                   = tostring(var.api_memory)
  execution_role_arn       = aws_iam_role.execution.arn
  container_definitions = jsonencode([{
    name = "api", image = local.api_image, essential = true,
    portMappings = [{ containerPort = 8080, protocol = "tcp" }],
    environment = [
      { name = "ASPNETCORE_URLS", value = "http://+:8080" },
      { name = "Keycloak__Authority", value = local.realm_url },
      { name = "Keycloak__MetadataAddress", value = "${local.realm_url}/.well-known/openid-configuration" },
      { name = "Keycloak__ValidIssuer", value = local.realm_url },
      { name = "Keycloak__Audience", value = "crm-api" },
      { name = "Keycloak__RequireHttpsMetadata", value = var.certificate_arn != "" ? "true" : "false" }
    ],
    secrets = [{ name = "ConnectionStrings__Crm", valueFrom = aws_secretsmanager_secret_version.connection.arn }],
    logConfiguration = { logDriver = "awslogs", options = merge(local.log_base, { "awslogs-group" = aws_cloudwatch_log_group.api.name }) }
  }])
  tags = local.tags
}
resource "aws_ecs_task_definition" "web" {
  family                   = "${local.name}-web"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = tostring(var.web_cpu)
  memory                   = tostring(var.web_memory)
  execution_role_arn       = aws_iam_role.execution.arn
  container_definitions = jsonencode([{
    name = "web", image = local.web_image, essential = true,
    portMappings = [{ containerPort = 80, protocol = "tcp" }],
    logConfiguration = { logDriver = "awslogs", options = merge(local.log_base, { "awslogs-group" = aws_cloudwatch_log_group.web.name }) }
  }])
  tags = local.tags
}
resource "aws_ecs_task_definition" "keycloak" {
  family                   = "${local.name}-keycloak"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = tostring(var.keycloak_cpu)
  memory                   = tostring(var.keycloak_memory)
  execution_role_arn       = aws_iam_role.execution.arn
  container_definitions = jsonencode([{
    name = "keycloak", image = local.keycloak_image, essential = true,
    command = ["start", "--health-enabled=true"],
    portMappings = [{ containerPort = 8080, protocol = "tcp" }],
    environment = [
      { name = "PUBLIC_BASE_URL", value = local.public_base_url },
      { name = "KC_BOOTSTRAP_ADMIN_USERNAME", value = var.keycloak_admin_username },
      { name = "KC_DB", value = "postgres" },
      { name = "KC_DB_URL", value = "jdbc:postgresql://${aws_db_instance.main.address}:5432/crm" },
      { name = "KC_DB_SCHEMA", value = "keycloak" },
      { name = "KC_HTTP_ENABLED", value = "true" },
      { name = "KC_HTTP_RELATIVE_PATH", value = "/auth" },
      { name = "KC_HOSTNAME", value = "${local.public_base_url}/auth" },
      { name = "KC_PROXY_HEADERS", value = "xforwarded" },
      { name = "KC_HEALTH_ENABLED", value = "true" },
      { name = "KC_HOSTNAME_STRICT", value = "false" }
    ],
    secrets = [
      { name = "KC_DB_USERNAME", valueFrom = "${aws_secretsmanager_secret.db.arn}:username::" },
      { name = "KC_DB_PASSWORD", valueFrom = "${aws_secretsmanager_secret.db.arn}:password::" },
      { name = "KC_BOOTSTRAP_ADMIN_PASSWORD", valueFrom = "${aws_secretsmanager_secret.db.arn}:keycloak_admin_password::" }
    ],
    logConfiguration = { logDriver = "awslogs", options = merge(local.log_base, { "awslogs-group" = aws_cloudwatch_log_group.keycloak.name }) }
  }])
  tags = local.tags
}
resource "aws_ecs_task_definition" "migrations" {
  family                   = "${local.name}-migrations"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.execution.arn
  container_definitions = jsonencode([{
    name = "migrations", image = local.migrations_image, essential = true,
    secrets = [{ name = "ConnectionStrings__Crm", valueFrom = aws_secretsmanager_secret_version.connection.arn }],
    logConfiguration = { logDriver = "awslogs", options = merge(local.log_base, { "awslogs-group" = aws_cloudwatch_log_group.migrations.name }) }
  }])
  tags = local.tags
}

resource "aws_ecs_service" "api" {
  name            = "api"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 1
  launch_type     = "FARGATE"
  network_configuration {
    subnets          = [for subnet in aws_subnet.public : subnet.id]
    security_groups  = [aws_security_group.tasks.id]
    assign_public_ip = true
  }
  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }
  depends_on = [aws_lb_listener_rule.api, aws_iam_role_policy.secrets]
  tags       = local.tags
}
resource "aws_ecs_service" "web" {
  name            = "web"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.web.arn
  desired_count   = 1
  launch_type     = "FARGATE"
  network_configuration {
    subnets          = [for subnet in aws_subnet.public : subnet.id]
    security_groups  = [aws_security_group.web_tasks.id]
    assign_public_ip = true
  }
  load_balancer {
    target_group_arn = aws_lb_target_group.web.arn
    container_name   = "web"
    container_port   = 80
  }
  depends_on = [aws_lb_listener.http]
  tags       = local.tags
}
resource "aws_ecs_service" "keycloak" {
  name            = "keycloak"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.keycloak.arn
  desired_count   = 1
  launch_type     = "FARGATE"
  health_check_grace_period_seconds = 180
  network_configuration {
    subnets          = [for subnet in aws_subnet.public : subnet.id]
    security_groups  = [aws_security_group.tasks.id]
    assign_public_ip = true
  }
  load_balancer {
    target_group_arn = aws_lb_target_group.keycloak.arn
    container_name   = "keycloak"
    container_port   = 8080
  }
  depends_on = [aws_lb_listener_rule.keycloak, aws_iam_role_policy.secrets]
  tags       = local.tags
}
