# GitHub Self-Hosted Runner Setup for Windows

This document provides step-by-step instructions for setting up a GitHub self-hosted runner on Windows 11 for the MovieReview deployment workflow.

## Prerequisites

### Windows Environment
- **Windows 11** (or Windows Server 2019+)
- **Administrator privileges** required
- **PowerShell 5.1+** or **PowerShell 7+**
- **Internet connectivity** for downloading runner and accessing GitHub

### Required Software
- **IIS (Internet Information Services)** with ASP.NET Core hosting bundle
- **.NET 9.0 SDK** (for building applications)
- **PowerShell WebAdministration module** (usually included with IIS)
- **Git** (for code checkout)

### Optional (MovieReview specific)
- **FFmpeg** (for audio processing - place in PATH)
- **MongoDB** (or connection to remote MongoDB instance)

## Runner Installation

### Step 1: Create Runner Directory
```powershell
# Create a dedicated directory for the GitHub runner
New-Item -Path "C:\actions-runner" -ItemType Directory -Force
Set-Location "C:\actions-runner"
```

### Step 2: Download GitHub Actions Runner
```powershell
# Download the latest runner package (check GitHub for current version)
$runnerVersion = "2.311.0"  # Update this to latest version
Invoke-WebRequest -Uri "https://github.com/actions/runner/releases/download/v$runnerVersion/actions-runner-win-x64-$runnerVersion.zip" -OutFile "actions-runner-win-x64.zip"

# Extract the runner
Expand-Archive -Path "actions-runner-win-x64.zip" -DestinationPath . -Force
Remove-Item "actions-runner-win-x64.zip"
```

### Step 3: Configure the Runner
```powershell
# Navigate to your GitHub repository settings:
# https://github.com/Choochoo/MovieReviewForSelfHosting/settings/actions/runners/new

# Run the configuration script (you'll need the token from GitHub)
.\config.cmd --url https://github.com/Choochoo/MovieReviewForSelfHosting --token YOUR_TOKEN_FROM_GITHUB

# When prompted, configure:
# - Runner name: moviereview-windows-runner
# - Runner group: Default
# - Labels: windows,iis,deployment,self-hosted
# - Work folder: _work (default)
```

### Step 4: Install as Windows Service
```powershell
# Install the runner as a Windows service (recommended for production)
.\svc.sh install

# Start the service
.\svc.sh start

# Verify service is running
Get-Service "GitHub Actions Runner*"
```

## IIS Configuration Verification

### Verify IIS Installation
```powershell
# Check if IIS is installed
Get-WindowsFeature -Name IIS-WebServerRole

# If not installed, install IIS with required features
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-HttpErrors, IIS-HttpLogging, IIS-HttpCompressionStatic, IIS-Security, IIS-RequestFiltering, IIS-NetFxExtensibility45, IIS-ISAPIExtensions, IIS-ISAPIFilter, IIS-ASPNET45, IIS-NetFxExtensibility, IIS-ISAPIExtensions, IIS-ISAPIFilter, IIS-ASPNET
```

### Install ASP.NET Core Hosting Bundle
```powershell
# Download and install ASP.NET Core 9.0 Hosting Bundle
$hostingBundleUrl = "https://download.microsoft.com/download/2/1/2/212F7C4D-4879-4A8B-A406-C1A0D0C7D1B9/dotnet-hosting-9.0.0-win.exe"
Invoke-WebRequest -Uri $hostingBundleUrl -OutFile "dotnet-hosting-bundle.exe"
Start-Process -FilePath "dotnet-hosting-bundle.exe" -ArgumentList "/quiet" -Wait

# Restart IIS to load the new module
iisreset
```

### Verify PowerShell WebAdministration
```powershell
# Test WebAdministration module
Import-Module WebAdministration
Get-Website  # Should list existing websites
```

## Create IIS Sites and App Pools

### Create Application Pools
```powershell
# Import WebAdministration module
Import-Module WebAdministration

# Create application pools for each instance
New-WebAppPool -Name "MovieReview" -Force
New-WebAppPool -Name "MovieReviewKids" -Force  
New-WebAppPool -Name "MovieReviewDemo" -Force

# Configure application pools
Set-ItemProperty -Path "IIS:\AppPools\MovieReview" -Name "processModel.identityType" -Value "ApplicationPoolIdentity"
Set-ItemProperty -Path "IIS:\AppPools\MovieReview" -Name "managedRuntimeVersion" -Value ""

Set-ItemProperty -Path "IIS:\AppPools\MovieReviewKids" -Name "processModel.identityType" -Value "ApplicationPoolIdentity" 
Set-ItemProperty -Path "IIS:\AppPools\MovieReviewKids" -Name "managedRuntimeVersion" -Value ""

Set-ItemProperty -Path "IIS:\AppPools\MovieReviewDemo" -Name "processModel.identityType" -Value "ApplicationPoolIdentity"
Set-ItemProperty -Path "IIS:\AppPools\MovieReviewDemo" -Name "managedRuntimeVersion" -Value ""
```

### Create IIS Websites
```powershell
# Create deployment directories
New-Item -Path "C:\inetpub\wwwroot\MovieReview" -ItemType Directory -Force
New-Item -Path "C:\inetpub\wwwroot\MovieReviewKids" -ItemType Directory -Force
New-Item -Path "C:\inetpub\wwwroot\MovieReviewDemo" -ItemType Directory -Force

# Create websites
New-Website -Name "MovieReview" -Port 5010 -PhysicalPath "C:\inetpub\wwwroot\MovieReview" -ApplicationPool "MovieReview"
New-Website -Name "MovieReviewKids" -Port 5012 -PhysicalPath "C:\inetpub\wwwroot\MovieReviewKids" -ApplicationPool "MovieReviewKids"
New-Website -Name "MovieReviewDemo" -Port 5015 -PhysicalPath "C:\inetpub\wwwroot\MovieReviewDemo" -ApplicationPool "MovieReviewDemo"

# Set proper permissions
icacls "C:\inetpub\wwwroot\MovieReview" /grant "IIS_IUSRS:(OI)(CI)F" /T
icacls "C:\inetpub\wwwroot\MovieReviewKids" /grant "IIS_IUSRS:(OI)(CI)F" /T
icacls "C:\inetpub\wwwroot\MovieReviewDemo" /grant "IIS_IUSRS:(OI)(CI)F" /T
```

## Security Configuration


## Testing the Setup

### Test Runner Connectivity
```powershell
# Check runner status
.\run.cmd --check

# View runner logs
Get-Content -Path "_diag\Runner_*.log" -Tail 20
```

### Test IIS Sites
```

### Test Build Environment
```powershell
# Verify .NET SDK is available
dotnet --version
dotnet --list-runtimes

# Test Git access
git --version

# Test PowerShell WebAdministration
Import-Module WebAdministration
Get-Website
```

## Troubleshooting

### Common Issues

**Runner not appearing in GitHub:**
- Check network connectivity
- Verify token hasn't expired
- Check Windows Event Logs for service errors

**IIS deployment failures:**
- Verify IIS_IUSRS permissions on deployment directories
- Check that app pools are running with correct .NET version
- Ensure ASP.NET Core hosting bundle is installed

**Build failures:**
- Verify .NET 9.0 SDK is installed
- Check that runner has access to internet for package restoration
- Ensure adequate disk space for build artifacts

**Permission errors:**
- Run runner configuration as Administrator
- Verify service account has necessary privileges
- Check directory permissions with `icacls`

### Logs and Diagnostics
```powershell
# Runner logs
Get-ChildItem -Path "_diag" | Sort-Object LastWriteTime -Descending | Select-Object -First 5

# Windows Event Logs
Get-EventLog -LogName Application -Source "GitHub Actions Runner*" -Newest 10

# IIS logs  
Get-ChildItem -Path "C:\inetpub\logs\LogFiles" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 5
```

## Maintenance

### Regular Tasks
- **Weekly**: Check runner logs for errors
- **Monthly**: Update runner to latest version
- **Quarterly**: Rotate service account passwords (if using dedicated account)

### Updates
```powershell
# Stop runner service
.\svc.sh stop

# Download and install new runner version
# (Follow Step 2 installation process)

# Start service
.\svc.sh start
```

### Monitoring
- Monitor disk space in runner work directory
- Check IIS application pool health
- Monitor network connectivity to GitHub
- Review deployment success/failure rates in GitHub Actions

## Security Best Practices

1. **Least Privilege**: Runner service account should have minimal necessary permissions
2. **Network Isolation**: Consider running on isolated network segment
3. **Regular Updates**: Keep Windows, .NET, and runner updated
4. **Monitoring**: Set up alerts for failed deployments
5. **Backup**: Regular backup of runner configuration and IIS settings
6. **Access Control**: Limit who can modify runner configuration

## Next Steps

After completing this setup:
1. Configure repository secrets (see `GITHUB_SECRETS.md`)
2. Test a manual deployment workflow trigger
3. Monitor the first few deployments closely
4. Set up additional monitoring and alerting as needed
