#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TF="$ROOT/infra/terraform"
REGION="${AWS_REGION:-ap-southeast-2}"
TAG="${IMAGE_TAG:-$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || date +%Y%m%d%H%M%S)}"
AOT="${ENABLE_NATIVE_AOT:-false}"

for tool in aws terraform docker jq; do
  command -v "$tool" >/dev/null || { echo "Missing required command: $tool" >&2; exit 1; }
done
aws sts get-caller-identity >/dev/null
echo "[1/7] Provisioning AWS infrastructure in $REGION ..."
terraform -chdir="$TF" init
terraform -chdir="$TF" validate
terraform -chdir="$TF" apply -auto-approve -var="aws_region=$REGION" -var="api_image_tag=$TAG" -var="web_image_tag=$TAG" -var="migrations_image_tag=$TAG"

API_REPO="$(terraform -chdir="$TF" output -raw api_ecr_repository)"
WEB_REPO="$(terraform -chdir="$TF" output -raw web_ecr_repository)"
MIG_REPO="$(terraform -chdir="$TF" output -raw migrations_ecr_repository)"
KC_REPO="$(terraform -chdir="$TF" output -raw keycloak_ecr_repository)"
BASE_URL="$(terraform -chdir="$TF" output -raw public_url)"
ACCOUNT="$(aws sts get-caller-identity --query Account --output text)"
REGISTRY="${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"

echo "[2/7] Authenticating to ECR ..."
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$REGISTRY"

echo "[3/7] Building and pushing the API, web, migration and Keycloak images ..."
if [[ "$AOT" == "true" ]]; then
  docker build --platform linux/amd64 --build-arg ENABLE_NATIVE_AOT=true --target aot -t "$API_REPO:$TAG" -f "$ROOT/backend/Dockerfile" "$ROOT/backend"
else
  docker build --platform linux/amd64 --target jit -t "$API_REPO:$TAG" -f "$ROOT/backend/Dockerfile" "$ROOT/backend"
fi
docker build --platform linux/amd64 -t "$MIG_REPO:$TAG" -f "$ROOT/backend.Migrations/Dockerfile" "$ROOT/backend.Migrations"
docker build --platform linux/amd64 --build-arg "VITE_KEYCLOAK_URL=$BASE_URL/auth" --build-arg VITE_API_BASE= -t "$WEB_REPO:$TAG" -f "$ROOT/frontend/Dockerfile" "$ROOT/frontend"
docker build --platform linux/amd64 -t "$KC_REPO:latest" -f "$ROOT/infra/keycloak/Dockerfile" "$ROOT/infra/keycloak"
for image in "$API_REPO:$TAG" "$MIG_REPO:$TAG" "$WEB_REPO:$TAG" "$KC_REPO:latest"; do docker push "$image"; done

echo "[4/7] Waiting for the database ..."
DB_ID="$(terraform -chdir="$TF" output -raw db_instance_identifier)"
aws rds wait db-instance-available --region "$REGION" --db-instance-identifier "$DB_ID"

CLUSTER="$(terraform -chdir="$TF" output -raw cluster_name)"
MIG_TASK="$(terraform -chdir="$TF" output -raw migration_task_definition)"
SUBNETS="$(terraform -chdir="$TF" output -json public_subnet_ids | jq -r 'join(",")')"
TASK_SG="$(terraform -chdir="$TF" output -raw task_security_group_id)"
NETWORK="awsvpcConfiguration={subnets=[$(echo "$SUBNETS" | sed 's/,/,/g')],securityGroups=[$TASK_SG],assignPublicIp=ENABLED}"

echo "[5/7] Applying EF Core migrations to RDS ..."
TASK_ARN="$(aws ecs run-task --region "$REGION" --cluster "$CLUSTER" --task-definition "$MIG_TASK" --launch-type FARGATE --network-configuration "$NETWORK" --query 'tasks[0].taskArn' --output text)"
if [[ -z "$TASK_ARN" || "$TASK_ARN" == "None" ]]; then echo "Could not start the EF migration task" >&2; exit 1; fi
aws ecs wait tasks-stopped --region "$REGION" --cluster "$CLUSTER" --tasks "$TASK_ARN"
EXIT_CODE="$(aws ecs describe-tasks --region "$REGION" --cluster "$CLUSTER" --tasks "$TASK_ARN" --query 'tasks[0].containers[0].exitCode' --output text)"
if [[ "$EXIT_CODE" != "0" ]]; then
  aws ecs describe-tasks --region "$REGION" --cluster "$CLUSTER" --tasks "$TASK_ARN" --query 'tasks[0].containers[0].reason' --output text >&2
  echo "EF Core migration failed. Inspect the migration CloudWatch log group." >&2
  exit 1
fi

echo "[6/7] Rolling out ECS services ..."
for SERVICE in api web keycloak; do
  aws ecs update-service --region "$REGION" --cluster "$CLUSTER" --service "$SERVICE" --force-new-deployment >/dev/null
done
aws ecs wait services-stable --region "$REGION" --cluster "$CLUSTER" --services api web keycloak

echo "[7/7] Deployment submitted successfully."
echo "CRM: $BASE_URL"
echo "Keycloak admin user: $(terraform -chdir="$TF" output -raw keycloak_admin_username)"
SECRET_ARN="$(terraform -chdir="$TF" output -raw database_secret_arn)"
echo "Retrieve the generated Keycloak admin password with:"
echo "aws secretsmanager get-secret-value --region $REGION --secret-id '$SECRET_ARN' --query SecretString --output text | jq -r .keycloak_admin_password"
echo "Health endpoint: $BASE_URL/health"
