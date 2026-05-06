$Region = "us-west-2"
$AccountId = "663396194001"
$RoleName = "MeetingJudgeAgentRole"
$StackName = "meeting-judge-agent"
$ImageUri = "$AccountId.dkr.ecr.$Region.amazonaws.com/meeting-judge-agent:latest"

# Create temp directory
$TempDir = Join-Path $env:TEMP "meeting-judge-deploy"
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

# 1. Trust policy
$trustPolicy = @'
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": { "Service": "bedrock-agentcore.amazonaws.com" },
      "Action": "sts:AssumeRole",
      "Condition": {
        "StringEquals": { "aws:SourceAccount": "663396194001" },
        "ArnLike": { "aws:SourceArn": "arn:aws:bedrock-agentcore:us-west-2:663396194001:*" }
      }
    }
  ]
}
'@
$trustPolicyFile = Join-Path $TempDir "trust-policy.json"
$trustPolicy | Out-File -Encoding utf8 -FilePath $trustPolicyFile

Write-Host "Creating IAM role..." -ForegroundColor Cyan
aws iam create-role --role-name $RoleName --assume-role-policy-document "file://$trustPolicyFile" --region $Region

# 2. Attach ECR read policy
Write-Host "Attaching ECR read policy..." -ForegroundColor Cyan
aws iam attach-role-policy --role-name $RoleName --policy-arn arn:aws:iam::aws:policy/AmazonEC2ContainerRegistryReadOnly

# 3. Inline permissions policy
$permissionsPolicy = @"
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["bedrock:InvokeModel", "bedrock:InvokeModelWithResponseStream"],
      "Resource": [
        "arn:aws:bedrock:$Region::foundation-model/*",
        "arn:aws:bedrock:::foundation-model/*",
        "arn:aws:bedrock:*:${AccountId}:inference-profile/*"
      ]
    },
    {
      "Effect": "Allow",
      "Action": ["logs:CreateLogGroup", "logs:CreateLogStream", "logs:PutLogEvents"],
      "Resource": "arn:aws:logs:${Region}:${AccountId}:*"
    }
  ]
}
"@
$permsPolicyFile = Join-Path $TempDir "permissions-policy.json"
$permissionsPolicy | Out-File -Encoding utf8 -FilePath $permsPolicyFile

Write-Host "Adding inline permissions policy..." -ForegroundColor Cyan
aws iam put-role-policy --role-name $RoleName --policy-name MeetingJudgePolicy --policy-document "file://$permsPolicyFile"

# 4. Wait for role propagation
Write-Host "Waiting 10s for IAM role propagation..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# 5. CloudFormation template
$cfnTemplate = @"
{
  "Resources": {
    "Runtime": {
      "Type": "AWS::BedrockAgentCore::Runtime",
      "Properties": {
        "AgentRuntimeName": "MeetingJudgeAgent",
        "RoleArn": "arn:aws:iam::${AccountId}:role/$RoleName",
        "AgentRuntimeArtifact": {
          "ContainerConfiguration": {
            "ContainerUri": "$ImageUri"
          }
        },
        "NetworkConfiguration": {
          "NetworkMode": "PUBLIC"
        }
      }
    }
  },
  "Outputs": {
    "RuntimeArn": {
      "Value": { "Fn::GetAtt": ["Runtime", "AgentRuntimeArn"] }
    }
  }
}
"@
$cfnFile = Join-Path $TempDir "cfn-template.json"
$cfnTemplate | Out-File -Encoding utf8 -FilePath $cfnFile

Write-Host "Creating CloudFormation stack..." -ForegroundColor Cyan
aws cloudformation create-stack --stack-name $StackName --region $Region --template-body "file://$cfnFile"

Write-Host "`nStack creation initiated. Run this to check status:" -ForegroundColor Green
Write-Host "  aws cloudformation describe-stacks --stack-name $StackName --region $Region --query `"Stacks[0].StackStatus`" --output text"
Write-Host "`nOnce CREATE_COMPLETE, get the runtime ARN:" -ForegroundColor Green
Write-Host "  aws cloudformation describe-stacks --stack-name $StackName --region $Region --query `"Stacks[0].Outputs[?OutputKey=='RuntimeArn'].OutputValue`" --output text"
Write-Host "`nThen invoke with:" -ForegroundColor Green
Write-Host "  aws bedrock-agent-runtime invoke-agent-runtime --agent-runtime-arn <ARN> --payload '{`"prompt`": `"Meeting: Weekly Sync, Attendees: 12, Duration: 60min, Agenda: align on stuff`"}' --region $Region --query output --output text"
