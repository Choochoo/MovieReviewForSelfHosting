# GitHub Repository Secrets Configuration

This document lists all repository secrets required for the GitHub Actions deployment workflow.

## Required Repository Secrets

Navigate to your GitHub repository → Settings → Secrets and variables → Actions to add these secrets.

### Database Configuration

| Secret Name | Description | Example Value |
|-------------|-------------|---------------|
| `MONGODB_CONNECTION_STRING` | MongoDB connection string for all instances | `mongodb://localhost:27017/moviereview` |

### AI Services API Keys

| Secret Name | Description | Notes |
|-------------|-------------|-------|
| `GLADIA_API_KEY` | Gladia API key for audio transcription | Required for audio processing |
| `OPENAI_API_KEY` | OpenAI API key for AI analysis | Optional - for OpenAI-based analysis |
| `CLAUDE_API_KEY` | Anthropic Claude API key for AI analysis | Optional - for Claude-based analysis |

### Optional Services

| Secret Name | Description | Notes |
|-------------|-------------|-------|
| `FACEBOOK_CHAT_URL` | Facebook Messenger webhook URL | Optional - for Facebook integration |
| `TMDB_API_KEY` | The Movie Database API key | Optional - for movie metadata |

## Environment-Specific Secrets

If you need different configurations for staging vs production, you can set up environment-specific secrets:

### Staging Environment
- Prefix all secret names with `STAGING_` (e.g., `STAGING_MONGODB_CONNECTION_STRING`)

### Production Environment  
- Prefix all secret names with `PRODUCTION_` (e.g., `PRODUCTION_MONGODB_CONNECTION_STRING`)

## How Secrets Are Used

The GitHub Actions workflow automatically injects these secrets as environment variables during deployment:

1. **Build Phase**: No secrets needed - only builds and tests the application
2. **Security Scan**: Checks for accidentally committed secrets
3. **Deployment Phase**: Secrets are configured on the self-hosted runner and made available to the application

## Security Best Practices

### ✅ Do:
- Use separate API keys for staging and production
- Regularly rotate API keys
- Use read-only database connections where possible
- Monitor API usage to detect unauthorized access
- Use environment-specific prefixes for different deployment targets

### ❌ Don't:
- Commit secrets to the repository 
- Use production API keys in staging
- Share API keys between different applications
- Store secrets in configuration files

## Secret Validation

The deployment workflow includes validation to ensure:
- Required secrets are present
- API keys can authenticate successfully  
- Database connections are working
- No secrets are accidentally deployed to the web directory

## Troubleshooting

### Common Issues:

**"Secret not found" errors:**
- Verify the secret name matches exactly (case-sensitive)
- Check that the secret is set at the repository level, not environment level
- Ensure you have admin access to the repository

**"Authentication failed" errors:**  
- Verify the API key is correct and hasn't expired
- Check that the API key has the necessary permissions
- Test the API key manually before adding to GitHub

**"Database connection failed" errors:**
- Ensure the MongoDB connection string is correct
- Verify the database server is accessible from the self-hosted runner
- Check that the database user has the necessary permissions

## Instance-Specific Configuration

Each MovieReview instance (Adult, Kids, Demo) uses the same secrets but maintains separate databases:

- **Adult Instance**: `moviereview_adult` database
- **Kids Instance**: `moviereview_kids` database  
- **Demo Instance**: `moviereview_demo` database

The GitHub Actions workflow automatically appends the instance name to the database name in the connection string.

## Monitoring Secret Usage

To monitor how secrets are being used:

1. **API Usage**: Check your API provider dashboards for usage statistics
2. **Database Connections**: Monitor MongoDB logs for connection attempts
3. **GitHub Actions**: Review workflow logs (secrets are masked automatically)
4. **Application Logs**: Check application logs on the deployment server

## Adding New Secrets

When adding new secrets to the application:

1. Update the `SecretsManager` class to handle the new secret
2. Add the secret to this documentation
3. Update the GitHub Actions workflow if the secret is needed during deployment
4. Add the secret to your GitHub repository settings
5. Test the deployment to ensure the new secret is working correctly

## Secret Rotation

Regular secret rotation schedule:

- **API Keys**: Every 90 days or immediately if compromised
- **Database Passwords**: Every 180 days
- **Integration Tokens**: Every 60 days

When rotating secrets:
1. Generate new secret
2. Update in GitHub repository settings  
3. Deploy to ensure new secret works
4. Revoke old secret
5. Update documentation with rotation date