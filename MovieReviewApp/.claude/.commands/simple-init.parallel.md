# Simple Init Parallel for Blazor/.NET

Initialize three parallel git worktree directories for concurrent development.

## Variables

FEATURE_NAME: $ARGUMENTS

## Execute these tasks

CREATE new directory `trees/`

> Execute these steps in parallel for concurrency
>
> Use absolute paths for all commands

CREATE first worktree:
- RUN `git worktree add -b FEATURE_NAME-1 ./trees/FEATURE_NAME-1`
- COPY `appsettings.json` to `./trees/FEATURE_NAME-1/appsettings.json`
- RUN `cd ./trees/FEATURE_NAME-1` then `dotnet restore`
- RUN `dotnet build`
- RUN `dotnet run --environment Development`

CREATE second worktree:
- RUN `git worktree add -b FEATURE_NAME-2 ./trees/FEATURE_NAME-2`
- COPY `appsettings.json` to `./trees/FEATURE_NAME-2/appsettings.json`
- RUN `cd ./trees/FEATURE_NAME-2` then `dotnet restore`
- RUN `dotnet build`
- RUN `dotnet run --environment Development`

CREATE third worktree:
- RUN `git worktree add -b FEATURE_NAME-3 ./trees/FEATURE_NAME-3`
- COPY `appsettings.json` to `./trees/FEATURE_NAME-3/appsettings.json`
- RUN `cd ./trees/FEATURE_NAME-3` then `dotnet restore`
- RUN `dotnet build`
- RUN `dotnet run --environment Development`

VERIFY setup by running `git worktree list`