#!/bin/bash

set -e # magical incantation to immediately exit if any command has a non-zero exit status. PEARL: it still won't fail if any of the following `set` commands fails.
set -u # magical incantation to mmediately exit if an undefined variable is referenced.
set -o pipefail # magical incantation to prevent pipelines from masking errors. (Use `command1 | command2 || true` to mask.)
shopt -s extglob # magical incantation to enable extended pattern matching.

set -x # magical incantation to enable echoing of commands for troubleshooting.

while [ $# -gt 0 ]; do
	case "$1" in
		ProjectName=*)
			project_name="${1#*=}"
			;;
		GitHubPackagesNuGetApiKey=*)
			github_packages_nuget_api_key="${1#*=}"
			;;
		*)
			printf "%s: Invalid argument: '%s'\n" "$0" "$1"
			exit 1
	esac
	shift
done

version=$(cat version.txt)
IFS=. read -r major minor patch <<< "$version"
printf "old version: %s.%s.%s\n" "$major" "$minor" "$patch"

# Bump major:
# version=$((major+1)).0.0
# Bump minor:
# version=$major.$((minor+1)).0
# Bump patch:
version=$major.$minor.$((patch+1))
printf "new version: %s.%s.%s\n" "$major" "$minor" "$patch"
printf "%s.%s.%s" "$major" "$minor" "$patch" > version.txt

# bash cicd-git-tag-bump.bash
# version_number=$(git describe --tags)

# PEARL: In GitHub, the output of `dotnet build` looks completely different from what it looks when building locally.
#        For example, the output of "Message" tasks is not shown, even when "Importance" is set to "High".
#        The "-ConsoleLoggerParameters:off" magical incantation corrects this problem.
# PEARL-ON-PEARL: The "-ConsoleLoggerParameters:off" magical incantation does not work when building locally; it only
#        works on github. Luckily, the "-TerminalLogger:off" magical incantation works both when building locally and
#        on github. It also comes in the form of an 'MSBUILDTERMINALLOGGER' environment variable.
MSBUILDTERMINALLOGGER=off

dotnet restore    -check
dotnet build      -check --configuration Debug --no-restore
dotnet test       -check --configuration Debug --no-build --verbosity normal

dotnet restore    -check
dotnet build      -check --configuration Release --no-restore
dotnet pack       -check --configuration Release --no-build

# Dry run
dry_run=true
if [ "$dry_run" = true ] ; then
	echo Dry run
else
	dotnet nuget push ${project_name}/bin/Release/*.nupkg --source https://nuget.pkg.github.com/MikeNakis/index.json --api-key ${github_packages_nuget_api_key}
fi

