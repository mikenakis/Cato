#!/bin/bash

set -e # magical incantation to immediately exit if any command has a non-zero exit status.
set =u # magical incantation to mmediately exit if an undefined variable is referenced.
set -o pipefail # magical incantation to prevent pipelines from masking errors. (Use `command1 | command2 || true` to mask.)
shopt -s extglob # magical incantation to enable extended pattern matching.

set -x # magical incantation to enable echoing of commands for troubleshooting.

# PEARL: In GitHub, the output of `dotnet build` looks completely different from what it looks when building locally.
#        For example, the output of "Message" tasks is not shown, even when "Importance" is set to "High".
#        The "-ConsoleLoggerParameters:off" magical incantation corrects this problem.
# PEARL-ON-PEARL: The "-ConsoleLoggerParameters:off" magical incantation does not work when building locally; it only
#        works on github. Luckily, the "-TerminalLogger:off" magical incantation works both when building locally and
#        on github.

dotnet restore    -TerminalLogger:off -check
dotnet build      -TerminalLogger:off -check --configuration Debug --no-restore
dotnet test       -TerminalLogger:off -check --configuration Debug --no-build --verbosity normal
