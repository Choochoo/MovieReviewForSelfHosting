## RUN
dotnet build

dotnet run --environment Development --audience Adult

dotnet run --environment Development --audience Kid

## READ
@CLAUDE.md
@MovieReviewApp.csproj
@Program.cs
@Components/Pages/
@Database/MongoDb.cs
@appsettings.json
@appsettings.Adult.json
@appsettings.Kid.json

## Remember
- Use `dotnet` CLI for all build and run tasks.
- App runs on http://localhost:5212 by default.
- MongoDB connection strings are set via environment variables.
- Audio/video uploads go to wwwroot/uploads/{Month}-{Year}/