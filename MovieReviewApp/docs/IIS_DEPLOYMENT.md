# IIS Deployment Guide for Movie Review App

This guide explains how to deploy multiple instances of the Movie Review App using IIS (Internet Information Services).

## Prerequisites

- IIS installed with ASP.NET Core Hosting Bundle
- .NET 8.0 Runtime
- MongoDB instance (local or cloud)
- Published Movie Review App files

## Single Instance Deployment

### 1. Publish the Application

```bash
dotnet publish -c Release -o ./publish
```

### 2. Create IIS Application

1. Open IIS Manager
2. Right-click on your site and select "Add Application"
3. Set the alias (e.g., "moviereview")
4. Set the physical path to your publish folder

### 3. Configure web.config

Edit the `web.config` file in your publish folder:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <remove name="aspNetCore" />
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" 
                  arguments=".\MovieReviewApp.dll --instance Default --port 5000" 
                  stdoutLogEnabled="true" 
                  stdoutLogFile=".\logs\stdout" 
                  hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

## Multiple Instance Deployment

To run multiple isolated instances on the same server:

### 1. Create Separate Folders

Create a folder structure like:
```
C:\inetpub\wwwroot\
├── moviereview-family\
├── moviereview-work\
└── moviereview-friends\
```

### 2. Copy Application Files

Copy the published files to each folder.

### 3. Configure Each Instance

Edit the `web.config` in each folder with unique instance names and ports:

#### Family Instance (moviereview-family/web.config)
```xml
<aspNetCore processPath="dotnet" 
            arguments=".\MovieReviewApp.dll --instance Family --port 5001" 
            stdoutLogEnabled="true" 
            stdoutLogFile=".\logs\stdout" 
            hostingModel="inprocess">
```

#### Work Instance (moviereview-work/web.config)
```xml
<aspNetCore processPath="dotnet" 
            arguments=".\MovieReviewApp.dll --instance Work --port 5002" 
            stdoutLogEnabled="true" 
            stdoutLogFile=".\logs\stdout" 
            hostingModel="inprocess">
```

#### Friends Instance (moviereview-friends/web.config)
```xml
<aspNetCore processPath="dotnet" 
            arguments=".\MovieReviewApp.dll --instance Friends --port 5003" 
            stdoutLogEnabled="true" 
            stdoutLogFile=".\logs\stdout" 
            hostingModel="inprocess">
```

### 4. Create IIS Applications

In IIS Manager:

1. Create applications for each instance:
   - Alias: `family` → Physical Path: `C:\inetpub\wwwroot\moviereview-family`
   - Alias: `work` → Physical Path: `C:\inetpub\wwwroot\moviereview-work`
   - Alias: `friends` → Physical Path: `C:\inetpub\wwwroot\moviereview-friends`

### 5. Configure Application Pools (Optional)

For better isolation, create separate application pools for each instance:

1. In IIS Manager, go to Application Pools
2. Add new application pool for each instance
3. Set .NET CLR version to "No Managed Code"
4. Set Managed pipeline mode to "Integrated"
5. Assign each application to its respective pool

## URL Structure

With this setup, your instances will be accessible at:
- `http://yourserver/family`
- `http://yourserver/work`
- `http://yourserver/friends`

## Using Different Ports

If you prefer different ports instead of URL paths:

### 1. Create Separate Sites

Instead of applications under one site, create separate IIS sites:

1. Each site bound to a different port:
   - Family Movies: Port 8001
   - Work Film Club: Port 8002
   - Friends Cinema: Port 8003

### 2. Configure Bindings

In each site's bindings:
- Type: http
- IP Address: All Unassigned (or specific IP)
- Port: (unique port number)
- Host name: (optional, for domain-based routing)

### 3. Update web.config Ports

Make sure the `--port` argument in web.config matches the IIS binding port.

## Reverse Proxy Configuration (Advanced)

For a cleaner URL structure with a reverse proxy:

### Using IIS URL Rewrite

1. Install URL Rewrite Module
2. Create inbound rules to route:
   - `/family/*` → `localhost:5001`
   - `/work/*` → `localhost:5002`
   - `/friends/*` → `localhost:5003`

### Using NGINX

```nginx
location /family/ {
    proxy_pass http://localhost:5001/;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection keep-alive;
    proxy_set_header Host $host;
    proxy_cache_bypass $http_upgrade;
}
```

## Troubleshooting

### Common Issues

1. **500.19 Error**: Check web.config syntax and IIS features
2. **502.5 Error**: Ensure .NET Runtime and ASP.NET Core Module are installed
3. **Port Conflicts**: Verify each instance has a unique port
4. **Permission Issues**: Grant IIS_IUSRS read/write access to the application folder

### Logging

Enable stdout logging in web.config for debugging:
```xml
stdoutLogEnabled="true"
stdoutLogFile=".\logs\stdout"
```

Check logs in the `logs` folder within each instance directory.

## Security Considerations

1. **HTTPS**: Configure SSL certificates for production
2. **Firewall**: Only open necessary ports
3. **Authentication**: Consider adding Windows Authentication or other auth methods
4. **Secrets**: Each instance maintains its own encrypted secrets.json file

## Maintenance

### Updating an Instance

1. Stop the application pool
2. Replace application files
3. Start the application pool

### Backing Up

Important files to backup per instance:
- Instance configuration: `%APPDATA%\MovieReviewApp\instances\{instance-name}\`
- Application logs: `logs\` folder
- MongoDB database for the instance

## Performance Tips

1. **Application Initialization**: Enable in IIS for faster first load
2. **Idle Timeout**: Increase or set to 0 to prevent app shutdown
3. **Recycling**: Configure appropriate recycling settings
4. **Memory**: Monitor and adjust application pool memory limits

---

For more information, see the main [README.md](../README.md) file.