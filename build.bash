#!/bin/bash

set -e # magical incantation to immediately exit if any command has a non-zero exit status. PEARL: it still won't fail if any of the following `set` commands fails.
set -u # magical incantation to mmediately exit if an undefined variable is referenced.
set -o pipefail # magical incantation to prevent pipelines from masking errors. (Use `command1 | command2 || true` to mask.)
shopt -s extglob # magical incantation to enable extended pattern matching.

# set -x # magical incantation to enable echoing commands for the purpose of troubleshooting.

function increment_version()
{
	local part_to_increment=$1
	local version=$2
	local major
	local minor
	local patch

	IFS=. read -r major minor patch <<< "$version"
	case "$part_to_increment" in
		"increment_major")
			major=$((major+1))
			minor=0
			patch=0
			;;
		"increment_minor")
			minor=$((minor+1))
			patch=0
			;;
		"increment_patch")
			patch=$((patch+1))
			;;
		*)
			printf "%s: Invalid argument: '%s'\n" "$0" "$1"
			return 1 # does this cause the script to fail?
	esac
	printf "%s.%s.%s" "$major" "$minor" "$patch"
}

function create_next_version()
{
	local part_to_increment=$1
	local version=$2

	local next_version=$(increment_version "$part_to_increment" "$version")
	printf "old version: %s new version: %s\n" "$version" "$next_version"
	printf "%s" "$next_version" > version.txt
	git add version.txt
	git commit --message="increment version from $version to $next_version"
}

function write_and_commit_version_file()
{
	local version=$1
	local next_version=$2

	printf "old version: %s new version: %s\n" "$version" "$next_version"
	printf "%s" "$next_version" > version.txt
	git add version.txt
	git commit --message="increment version from $version to $next_version"
}

function assert_no_untracked_files()
{
	local untracked_files=$(git ls-files -o --directory --exclude-standard --no-empty-directory)
	if [ "$untracked_files" != "" ]; then
		echo "You have untracked files:"
		echo $untracked_files
		echo "Please add, stage, and commit first."
		return 1
	fi
}

function assert_no_tracked_but_unstaged_changes()
{
	local unstaged_files=$(git diff-files --name-only)
	if [ "$unstaged_files" != "" ]; then
		echo "You have tracked but unstanged changes:"
		echo $unstaged_files
		echo "Please stage and commit first."
		return 1
	fi
}

function assert_no_staged_but_uncommitted_changes()
{
	local uncommitted_files=$(git diff-index --name-only --cached HEAD)
	if [ "$uncommitted_files" != "" ]; then
		echo "You have staged but uncommitted changes:"
		echo $uncommitted_files
		echo "Please unstage or commit first."
		return 1
	fi
}

function remove_quietly()
{
	local file=$1
	if [ -f "$file" ]; then
		rm "$file"
	fi
}

function command_auto_output_type_tool()
{
	dotnet test -check --configuration Debug --verbosity minimal
	remove_quietly ${project_name}/bin/Release/*.nupkg
	dotnet pack -check --configuration Release --property:PublicRelease=true
	dotnet nuget push ${project_name}/bin/Release/*.nupkg --source https://nuget.pkg.github.com/MikeNakis/index.json --api-key ${github_packages_nuget_api_key}
}

function command_manual_output_type_tool()
{
	remove_quietly ${project_name}/bin/Release/*.nupkg
	dotnet pack -check --configuration Release --property:PublicRelease=true
	dotnet nuget push ${project_name}/bin/Release/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${nuget_org_nuget_api_key}
}

function command_auto()
{
	assert_no_staged_but_uncommitted_changes

	case "$output_type" in
		"tool")
			command_auto_output_type_tool
			;;
		*)
			printf "%s: Invalid argument: '%s'\n" "$0" "$1"
			exit 1
	esac

	local version=$(cat version.txt)
	git tag "$version"
	create_next_version increment_patch $version
	printf "Pushing...\n"
	git push origin HEAD --tags
}

function command_manual()
{
	assert_no_staged_but_uncommitted_changes

	case "$output_type" in
		"tool")
			command_manual_output_type_tool
			;;
		*)
			printf "%s: Invalid argument: '%s'\n" "$0" "$1"
			exit 1
	esac

	local version=$(cat version.txt)
	git tag "$version"
	create_next_version increment_patch $version
	printf "Pushing...\n"
	git push origin HEAD --tags
}

while [ $# -gt 0 ]; do
	case "$1" in
		Command=*)
			command="${1#*=}"
			;;
		OutputType=*)
			output_type="${1#*=}"
			;;
		ProjectName=*)
			project_name="${1#*=}"
			;;
		GitHubPackagesNuGetApiKey=*)
			github_packages_nuget_api_key="${1#*=}"
			;;
		NuGetOrgNuGetApiKey=*)
			nuget_org_nuget_api_key="${1#*=}"
			;;
		*)
			printf "%s: Invalid argument: '%s'\n" "$0" "$1"
			exit 1
	esac
	shift
done

case "$command" in
	"auto")
		command_auto
		;;
	"manual")
		command_manual
		;;
	*)
		printf "%s: Invalid argument: '%s'\n" "$0" "$command"
		exit 1
esac
