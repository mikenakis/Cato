#!/bin/bash

# For some reason, this shebang does not work even though it is the recommended one:
# #!/usr/bin/env bash

# Magical incantations to enable unofficial bash strict mode, extended pattern matching, etc.
set -euo pipefail
shopt -s extglob
# set -x

# PEARL: In GitHub, the output of `dotnet build` looks completely different from what it looks when building locally.
#        For example, the output of "Message" tasks is not shown, even when "Importance" is set to "High".
#        The "-ConsoleLoggerParameters:off" magical incantation corrects this problem.
# PEARL-ON-PEARL: The "-ConsoleLoggerParameters:off" magical incantation does not work when building locally; it only
#        works on github. Luckily, the "-TerminalLogger:off" magical incantation works both when building locally and
#        on github.

dotnet restore    -TerminalLogger:off -check
dotnet build      -TerminalLogger:off -check --configuration Debug --no-restore
dotnet test       -TerminalLogger:off -check --configuration Debug --no-build --verbosity normal
