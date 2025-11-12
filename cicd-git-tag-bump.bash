#!/bin/bash

# original preposterously complex script: https://github.com/anothrNick/github-tag-action/blob/master/entrypoint.sh
# this is not in use yet, it is only an experiment.

set -e # mmediately exit if any command has a non-zero exit status. PEARL: it still won't fail if any of the following `set` commands fails.
set -u # mmediately exit if an undefined variable is referenced.
set -o pipefail # prevent errors in a pipeline from being masked. (Use `command1 | command2 || true` to mask.)

force_without_changes=true

# fetch tags.
printf "Fetching tags..."
git fetch --tags
printf " done.\n"

# find latest tag.
git_refs=$(git for-each-ref --sort=-v:refname --format '%(refname:lstrip=2)')
matching_tag_refs=$( (grep -E "^[0-9]+\.[0-9]+\.[0-9]+$" <<< "$git_refs") || true)
tag=$(head -n 1 <<< "$matching_tag_refs")
printf "Latest tag: '%s'\n" "$tag"

if [ -z "$tag" ]
then

	# if there is no latest tag, so start at initial version
    new=1.0.0
	echo "Setting tag to '${new}'"

else

	# get commit hash of tag
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
			echo "Skipping."
			exit 0
		else
			echo "Proceeding anyway."
		fi
	fi

	# split version tag into major, minor, patch
	IFS=. read -r major minor patch <<< $tag
	printf "major='%s' minor='%s' patch='%s'\n" "$major" "$minor" "$patch"

	# Bump major:
	# new=$((major+1)).0.0
	# Bump minor:
	# new=$major.$((minor+1)).0
	# Bump patch:
	new=$major.$minor.$((patch+1))

	echo "Bumping tag from '${tag}' to '${new}'"

fi

# dry run
# echo "Dry run"
# exit 0

# NOTE: in the original script, both `tag` and `push` was done with `--force`
# NOTE: in the original script, both `tag` and `push` was done with ` || exit 1`
git tag "$new"
git push origin "$new"
