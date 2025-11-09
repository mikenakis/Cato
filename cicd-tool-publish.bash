#!/bin/bash

# Magical incantations to enable unofficial bash strict mode, extended pattern matching, etc.
set -euo pipefail
shopt -s extglob
# set -x

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
			printf "$0: Invalid argument: '$1'\n"
			exit 1
	esac
	shift
done

# PEARL: In GitHub, the output of `dotnet build` looks completely different from what it looks when building locally.
#        For example, the output of "Message" tasks is not shown, even when "Importance" is set to "High".
#        The "-ConsoleLoggerParameters:off" magical incantation corrects this problem.
# PEARL-ON-PEARL: The "-ConsoleLoggerParameters:off" magical incantation does not work when building locally; it only
#        works on github. Luckily, the "-TerminalLogger:off" magical incantation works both when building locally and
#        on github.

dotnet restore    -TerminalLogger:off -check
dotnet build      -TerminalLogger:off -check --configuration Release --no-restore
dotnet pack       -TerminalLogger:off -check --configuration Release --no-build --property:PackageVersion=${Version}
dotnet nuget push ${ProjectName}/bin/Release/*.nupkg --source ${Address} --api-key ${ApiKey}
