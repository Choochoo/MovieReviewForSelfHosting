# Initialize parallel git worktree directories for Blazor/.NET

## Variables
FEATURE_NAME: $ARGUMENTS
NUMBER_OF_PARALLEL_WORKTREES: $ARGUMENTS

## Execute these commands
> Execute the loop in parallel with the Batch and Task tool

- create a new dir `trees/`
- for i in NUMBER_OF_PARALLEL_WORKTREES
  - RUN `git worktree add -b FEATURE_NAME-i ./trees/FEATURE_NAME-i`
  - COPY `appsettings.json` to `./trees/FEATURE_NAME-i/appsettings.json`
  - RUN `cd ./trees/FEATURE_NAME-i`, `dotnet restore`
  - RUN `dotnet build`
  - RUN `dotnet run --environment Development`
  - RUN `ls ./trees/FEATURE_NAME-i` to verify
- RUN `git worktree list` to verify all trees were created properly