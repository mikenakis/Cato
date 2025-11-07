#!/bin/bash

# For some reason, this shebang does not work even though it is the recommended one:
# #!/usr/bin/env bash

# Magical incantation to prevent silent failure if a command fails. (though this seems to be the default behavior.)
set -e

# Magical incantation to prevent silent failure if an undefined variable is used.
set -u

# Magical incantation to enable extended pattern matching.
shopt -s extglob

# Example:
# bash publish-tool.bash ProjectName=${{ github.event.repository.name }} Address=https://nuget.pkg.github.com/MikeNakis/index.json ApiKey=${{ secrets.MY_GITHUB_TOKEN }}

Version=$(git describe --tags)

while [ $# -gt 0 ]; do
  case "$1" in
      ProjectName=*)
      ProjectName="${1#*=}"
      ;;
      Address=*)
      Address="${1#*=}"
      ;;
      ApiKey=*)
      ApiKey="${1#*=}"
      ;;
      *)
      printf "Invalid argument: '$1'\n"
      exit 1
  esac
  shift
done

printf "ProjectName: ${ProjectName}; Version: ${Version}; Address: ${Address} ApiKey: ${ApiKey}\n"

# PEARL: In GitHub, the output of `dotnet build` looks completely different from what it looks when building locally.
#        For example, the output of "Message" tasks is not shown, even when "Importance" is set to "High".
#        The "-ConsoleLoggerParameters:off" magical incantation corrects this problem.
# PEARL-ON-PEARL: The "-ConsoleLoggerParameters:off" magical incantation does not work when building locally; ir only works on github.
#        Luckily, the "-TerminalLogger:off" magical incantation works both when building locally and on github.

dotnet restore    -TerminalLogger:off -check

dotnet build      -TerminalLogger:off -check --configuration Release --no-restore
dotnet pack       -TerminalLogger:off -check --configuration Release --no-build --property:PackageVersion=${Version}
dotnet nuget push ${ProjectName}/bin/Release/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${ApiKey}
