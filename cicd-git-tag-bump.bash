#!/bin/bash

# from https://github.com/anothrNick/github-tag-action/blob/master/entrypoint.sh
# this is for an experiment which has not been conducted yet.

set -euo pipefail

# config
default_semvar_bump=minor
dryrun=true
initial_version=0.0.0
force_without_changes=true

echo "*** CONFIGURATION ***"
echo -e "\tdefault_semvar_bump: ${default_semvar_bump}"
echo -e "\tdryrun: ${dryrun}"
echo -e "\tinitial_version: ${initial_version}"
echo -e "\tforce_without_changes: ${force_without_changes}"

current_branch=$(git rev-parse --abbrev-ref HEAD)
printf "Current branch: '%s'\n" "$current_branch"

git fetch --tags

tagFmt="^[0-9]+\.[0-9]+\.[0-9]+$"

# get the git refs from the repository
git_refs=$(git for-each-ref --sort=-v:refname --format '%(refname:lstrip=2)')
# get the git refs from the branch
# git_refs=$(git tag --list --merged HEAD --sort=-committerdate)

# get the latest tag that looks like a semver (with or without v)
matching_tag_refs=$( (grep -E "$tagFmt" <<< "$git_refs") || true)
tag=$(head -n 1 <<< "$matching_tag_refs")
printf "Latest tag: '%s'\n" "$tag"

# if there are none, start tags at initial version
if [ -z "$tag" ]
then
    tag="$initial_version"
fi

# get current commit hash for tag
tag_commit=$(git rev-list -n 1 "$tag" || true )
printf "tag commit: '%s'\n" "$tag_commit"

# get current commit hash
commit=$(git rev-parse HEAD)
printf "current commit: '%s'\n" "$commit"

# skip if there are no new commits
if [ "$tag_commit" == "$commit" ] && [ "$force_without_changes" == "false" ]
then
    echo "No new commits since previous tag. Skipping..."
    exit 0
else
	echo "No new commits, proceeding anyway..."
fi

log=$(git log "${tag_commit}".."${commit}" --format=%B)
printf "History:\n---\n%s\n---\n" "$log"

current_tag=${tag}
echo $tag

function semver()
{
	local method=$1
	local version=$2

	IFS=. read -r major minor patch <<EOF
$version
EOF

	case "$method" in
	major)
		printf "$((major+1)).0.0"
		;;
	minor)
		printf "$major.$((minor+1)).0"
		;;
	patch)
		printf "$major.$minor.$((patch+1))"
		;;
	*)
		printf "Invalid method: '%s'\n" "$method"
		exit -1
	esac
}

case "$log" in
    *#major* )
		new=$(semver major "${current_tag}")
		;;
    *#minor* )
		new=$(semver minor "${current_tag}")
		;;
    *#patch* )
		new=$(semver patch "${current_tag}")
		;;
    *#none* )
        echo "Default bump was set to none. Skipping..."
        exit 0;;
    * )
        if [ "$default_semvar_bump" == "none" ]
        then
            echo "Default bump was set to none. Skipping..."
            exit 0
        else
            new=$(semver "${default_semvar_bump}" "${current_tag}")
        fi
        ;;
esac

echo -e "Bumping tag from '${tag}' to '${new}'"

if $dryrun
then
    exit 0
fi

git tag -f "$new" || exit 1
git push -f origin "$new" || exit 1
