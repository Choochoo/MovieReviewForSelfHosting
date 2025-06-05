# IIS Deployment Guide for MovieReviewApp

This guide provides step-by-step instructions for deploying the MovieReviewApp to IIS (Internet Information Services).

## Prerequisites

- Windows Server or Windows with IIS installed
- .NET 8.0 Runtime and ASP.NET Core Hosting Bundle
- IIS configured with the ASP.NET Core Module
- MongoDB instance (local or remote)

## Publishing the Application

1. Open a terminal in the project root directory
2. Run the publish command:
   ```bash
   dotnet publish -c Release -o ./publish
   ```
3. This creates a `publish` folder containing all necessary files for deployment

## IIS Configuration

### 1. Create an Application Pool

1. Open IIS Manager
2. Right-click on "Application Pools" and select "Add Application Pool"
3. Configure:
   - Name: `MovieReviewApp`
   - .NET CLR Version: `No Managed Code`
   - Managed Pipeline Mode: `Integrated`

### 2. Create the Website

1. Right-click on "Sites" and select "Add Website"
2. Configure:
   - Site name: `MovieReviewApp`
   - Physical path: Point to your `publish` folder (e.g., `C:\inetpub\wwwroot\MovieReviewApp\publish`)
   - Binding: Configure your desired port (e.g., 80 or 443 for HTTPS)
   - Application Pool: Select `MovieReviewApp`

### 3. Configure web.config

After publishing, verify the `web.config` in your publish folder. It should contain:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" 
                  arguments=".\MovieReviewApp.dll --instance Default --port 5010" 
                  stdoutLogEnabled="true" 
                  stdoutLogFile=".\logs\stdout" 
                  hostingModel="InProcess" />
    </system.webServer>
  </location>
</configuration>
```

**Important**: Check for and remove any duplicate DLL entries in the arguments attribute.

## Critical Post-Publishing Steps

### 1. Set Folder Permissions

The IIS_IUSRS group requires full permissions to the publish folder for the application to write instance data and logs:

```cmd
icacls "C:\path\to\publish" /grant "IIS_IUSRS:(OI)(CI)F" /T
```

This command grants:
- Full control (F) to IIS_IUSRS
- Object inheritance (OI) for files
- Container inheritance (CI) for folders
- Applied recursively (/T) to all subdirectories

**Why this is needed**: The application needs to:
- Write and update instance configuration files
- Create and write to log files
- Store uploaded images and other user data
- Manage session data and temporary files

### 2. Create Required Directories

Create a `logs` directory in your publish folder:

```cmd
mkdir C:\path\to\publish\logs
```

### 3. Update IIS Physical Path

After publishing:
1. Open IIS Manager
2. Select your website
3. Click "Basic Settings"
4. Update the physical path to point to your publish folder
5. Click OK and restart the website

## Testing the Deployment

### Direct Testing (Recommended First Step)

Before troubleshooting IIS-specific issues, verify the application works by running it directly:

1. Open Command Prompt as Administrator
2. Navigate to your publish folder:
   ```cmd
   cd C:\path\to\publish
   ```
3. Run the application directly:
   ```cmd
   dotnet MovieReviewApp.dll --instance Default --port 5010
   ```
4. Open a browser and navigate to `http://localhost:5010`

If this works, you know the application is correctly published and any issues are IIS-specific.

## Troubleshooting Common Errors

### HTTP Error 500.30 - ASP.NET Core app failed to start

**Cause**: Missing instance configuration or incorrect startup parameters.

**Solutions**:
1. Verify the `--instance` parameter is included in web.config
2. Check that the instance name matches your configuration
3. Ensure the instance configuration file exists or the app can create it
4. Check folder permissions (see permissions section above)

### HTTP Error 500.31 - Failed to load ASP.NET Core runtime

**Cause**: Missing .NET runtime or hosting bundle.

**Solutions**:
1. Download and install the ASP.NET Core Hosting Bundle from Microsoft
2. Restart IIS after installation:
   ```cmd
   iisreset
   ```
3. Verify installation:
   ```cmd
   dotnet --list-runtimes
   ```

### UnauthorizedAccessException

**Cause**: IIS_IUSRS lacks necessary permissions.

**Solutions**:
1. Run the icacls command shown in the permissions section
2. Verify IIS_IUSRS has full control of the publish folder
3. Check Event Viewer for specific file/folder access denials
4. Ensure the logs directory exists and is writable

### Application Fails to Start - No Error Page

**Common Issues**:
1. **Missing logs directory**: Create it manually in the publish folder
2. **Incorrect physical path**: Verify IIS points to the publish folder, not the project root
3. **Port conflicts**: Ensure the port specified in web.config is available
4. **MongoDB connection**: Verify MongoDB is running and accessible

### Viewing Detailed Errors

To see detailed error messages during troubleshooting:

1. Edit `web.config` temporarily:
   ```xml
   <aspNetCore processPath="dotnet" 
               arguments=".\MovieReviewApp.dll --instance Default --port 5010" 
               stdoutLogEnabled="true" 
               stdoutLogFile=".\logs\stdout" 
               hostingModel="InProcess">
     <environmentVariables>
       <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" />
     </environmentVariables>
   </aspNetCore>
   ```
2. Remember to change back to "Production" once issues are resolved

## Environment Variables

You can set environment-specific configurations through IIS:

1. Select your website in IIS Manager
2. Double-click "Configuration Editor"
3. Navigate to `system.webServer/aspNetCore/environmentVariables`
4. Add required variables (e.g., connection strings, API keys)

## SSL/HTTPS Configuration

For production deployments:

1. Obtain an SSL certificate
2. Add an HTTPS binding to your website
3. Configure URL rewriting to redirect HTTP to HTTPS
4. Update your application settings to use HTTPS URLs

## Monitoring and Logs

- Application logs: `C:\path\to\publish\logs\`
- IIS logs: `C:\inetpub\logs\LogFiles\`
- Windows Event Viewer: Check Application and System logs

## Final Checklist

Before considering your deployment complete:

- [ ] Application published to publish folder
- [ ] IIS physical path updated to publish folder
- [ ] web.config arguments checked for duplicate DLL entries
- [ ] IIS_IUSRS granted full permissions to publish folder
- [ ] Logs directory created
- [ ] Application pool configured correctly
- [ ] Test direct execution with `dotnet MovieReviewApp.dll`
- [ ] Verify application accessible through IIS
- [ ] SSL certificate configured (for production)
- [ ] Environment set to Production (for production deployments)