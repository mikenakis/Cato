#!/bin/bash

set -euo pipefail

# config
default_semvar_bump=minor
default_branch=${DEFAULT_BRANCH:-$GITHUB_BASE_REF} # get the default branch from github runner env vars
with_v=${WITH_V:-false}
release_branches=master,main
dryrun=false
git_api_tagging=${GIT_API_TAGGING:-true}
initial_version=0.0.0
verbose=${VERBOSE:-false}
major_string_token=${MAJOR_STRING_TOKEN:-#major}
minor_string_token=${MINOR_STRING_TOKEN:-#minor}
patch_string_token=${PATCH_STRING_TOKEN:-#patch}
none_string_token=${NONE_STRING_TOKEN:-#none}
branch_history=${BRANCH_HISTORY:-compare}
force_without_changes=${FORCE_WITHOUT_CHANGES:-false}

echo "*** CONFIGURATION ***"
echo -e "\tDEFAULT_BUMP: ${default_semvar_bump}"
echo -e "\tDEFAULT_BRANCH: ${default_branch}"
echo -e "\tWITH_V: ${with_v}"
echo -e "\tRELEASE_BRANCHES: ${release_branches}"
echo -e "\tDRY_RUN: ${dryrun}"
echo -e "\tGIT_API_TAGGING: ${git_api_tagging}"
echo -e "\tINITIAL_VERSION: ${initial_version}"
echo -e "\tVERBOSE: ${verbose}"
echo -e "\tMAJOR_STRING_TOKEN: ${major_string_token}"
echo -e "\tMINOR_STRING_TOKEN: ${minor_string_token}"
echo -e "\tPATCH_STRING_TOKEN: ${patch_string_token}"
echo -e "\tNONE_STRING_TOKEN: ${none_string_token}"
echo -e "\tBRANCH_HISTORY: ${branch_history}"
echo -e "\tFORCE_WITHOUT_CHANGES: ${force_without_changes}"

current_branch=$(git rev-parse --abbrev-ref HEAD)

git fetch --tags

tagPrefix="v"

tagFmt="^$tagPrefix?[0-9]+\.[0-9]+\.[0-9]+$"

# get the git refs
tag_context=repo
git_refs=
case "$tag_context" in
    *repo*)
        git_refs=$(git for-each-ref --sort=-v:refname --format '%(refname:lstrip=2)')
        ;;
    *branch*)
        git_refs=$(git tag --list --merged HEAD --sort=-committerdate)
        ;;
    * ) echo "Unrecognised context"
        exit 1;;
esac

# get the latest tag that looks like a semver (with or without v)
matching_tag_refs=$( (grep -E "$tagFmt" <<< "$git_refs") || true)
tag=$(head -n 1 <<< "$matching_tag_refs")

# if there are none, start tags at initial version
if [ -z "$tag" ]
then
    tag="$tagPrefix$initial_version"
fi

# get current commit hash for tag
tag_commit=$(git rev-list -n 1 "$tag" || true )

# get current commit hash
commit=$(git rev-parse HEAD)

# skip if there are no new commits
if [ "$tag_commit" == "$commit" ] && [ "$force_without_changes" == "false" ]
then
    echo "No new commits since previous tag. Skipping..."
    exit 0
fi

# sanitize that the default_branch is set (via env var when running on PRs) else find it natively
if [ -z "${default_branch}" ] && [ "$branch_history" == "full" ]
then
    echo "The DEFAULT_BRANCH should be autodetected when tag-action runs on on PRs else must be defined, See: https://github.com/anothrNick/github-tag-action/pull/230, since is not defined we find it natively"
    default_branch=$(git branch -rl '*/master' '*/main' | cut -d / -f2)
    echo "default_branch=${default_branch}"
    # re check this
    if [ -z "${default_branch}" ]
    then
        echo "::error::DEFAULT_BRANCH must not be null, something has gone wrong."
        exit 1
    fi
fi

# get the merge commit message looking for #bumps
declare -A history_type=(
    ["last"]="$(git show -s --format=%B)" \
    ["full"]="$(git log "${default_branch}"..HEAD --format=%B)" \
    ["compare"]="$(git log "${tag_commit}".."${commit}" --format=%B)" \
)

log=${history_type[${branch_history}]}
printf "History:\n---\n%s\n---\n" "$log"

if [ -z "$tagPrefix" ]
then
  current_tag=${tag}
else
  current_tag="$(echo ${tag}| sed "s;${tagPrefix};;g")"
fi

case "$log" in
    *$major_string_token* ) new=${tagPrefix}$(semver -i major "${current_tag}"); part="major";;
    *$minor_string_token* ) new=${tagPrefix}$(semver -i minor "${current_tag}"); part="minor";;
    *$patch_string_token* ) new=${tagPrefix}$(semver -i patch "${current_tag}"); part="patch";;
    *$none_string_token* )
        echo "Default bump was set to none. Skipping..."
        setOutput "old_tag" "$tag"
        setOutput "new_tag" "$tag"
        setOutput "tag" "$tag"
        setOutput "part" "$default_semvar_bump"
        exit 0;;
    * )
        if [ "$default_semvar_bump" == "none" ]
        then
            echo "Default bump was set to none. Skipping..."
            setOutput "old_tag" "$tag"
            setOutput "new_tag" "$tag"
            setOutput "tag" "$tag"
            setOutput "part" "$default_semvar_bump"
            exit 0
        else
            new=${tagPrefix}$(semver -i "${default_semvar_bump}" "${current_tag}")
            part=$default_semvar_bump
        fi
        ;;
esac

echo -e "Bumping tag ${tag} - New tag ${new}"

# dry run exit without real changes
if $dryrun
then
    exit 0
fi

# Modify the tag creation part
git tag -f "$new" || exit 1

echo "EVENT: pushing tag $new to origin"

if $git_api_tagging
then
    # use git api to push
    dt=$(date '+%Y-%m-%dT%H:%M:%SZ')
    full_name=$GITHUB_REPOSITORY
    git_refs_url=$(jq .repository.git_refs_url "$GITHUB_EVENT_PATH" | tr -d '"' | sed 's/{\/sha}//g')

    echo "$dt: **pushing tag $new to repo $full_name"

    git_refs_response=$(
    curl -s -X POST "$git_refs_url" \
    -H "Authorization: token $GITHUB_TOKEN" \
    -d @- << EOF
{
    "ref": "refs/tags/$new",
    "sha": "$commit"
}
EOF
)

    git_ref_posted=$( echo "${git_refs_response}" | jq .ref | tr -d '"' )

    echo "::debug::${git_refs_response}"
    if [ "${git_ref_posted}" = "refs/tags/${new}" ]
    then
        exit 0
    else
        echo "::error::Tag was not created properly."
        exit 1
    fi
else
    # use git cli to push
    git push -f origin "$new" || exit 1
fi
