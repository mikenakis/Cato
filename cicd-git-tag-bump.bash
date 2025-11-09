#!/bin/bash

# from https://github.com/anothrNick/github-tag-action/blob/master/entrypoint.sh
# this is not in use yet, it is only an experiment.

set -euo pipefail

# config
force_without_changes=true

echo "*** CONFIGURATION ***"
echo -e "\tforce_without_changes: ${force_without_changes}"

current_branch=$(git rev-parse --abbrev-ref HEAD)
printf "Current branch: '%s'\n" "$current_branch"

printf "Fetching tags..."
git fetch --tags
printf " done.\n"

git_refs=$(git for-each-ref --sort=-v:refname --format '%(refname:lstrip=2)')
matching_tag_refs=$( (grep -E "^[0-9]+\.[0-9]+\.[0-9]+$" <<< "$git_refs") || true)
tag=$(head -n 1 <<< "$matching_tag_refs")
printf "Latest tag: '%s'\n" "$tag"

# if there is no latest tag, start at initial version
if [ -z "$tag" ]
then
    tag=0.0.0
fi

# get current commit hash for tag
tag_commit=$(git rev-list -n 1 "$tag" || true )
printf "tag commit: '%s'\n" "$tag_commit"

# get current commit hash
commit=$(git rev-parse HEAD)
printf "current commit: '%s'\n" "$commit"

# skip if there are no new commits
if [ "$tag_commit" == "$commit" ]
then
	echo "No new commits since previous tag."
	if [ "$force_without_changes" == "false" ]
	then
		echo "Skipping..."
		exit 0
	else
		echo "Proceeding anyway..."
	fi
fi

IFS=. read -r major minor patch <<< $tag
printf "major='%s' minor='%s' patch='%s'\n" "$major" "$minor" "$patch"

# Bump major:
# new=$((major+1)).0.0
# Bump minor:
# new=$major.$((minor+1)).0
# Bump patch:
new=$major.$minor.$((patch+1))

echo -e "Bumping tag from '${tag}' to '${new}'"

# dry run
exit 0

git tag -f "$new" || exit 1
git push -f origin "$new" || exit 1
