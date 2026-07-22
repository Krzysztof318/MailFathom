#!/usr/bin/env bash
set -euo pipefail

DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"
DOTNET_INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME}"
DOTNET_TOOLS_DIR="$DOTNET_CLI_HOME/.dotnet/tools"
DOTNET_INSTALL_SCRIPT_URL="${DOTNET_INSTALL_SCRIPT_URL:-https://dot.net/v1/dotnet-install.sh}"
DOTNET_EF_VERSION="${DOTNET_EF_VERSION:-10.0.10}"
ASPIRE_CLI_VERSION="${ASPIRE_CLI_VERSION:-13.4.6}"

export DOTNET_INSTALL_DIR
export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
export DOTNET_CLI_HOME
export PATH="$DOTNET_INSTALL_DIR:$DOTNET_TOOLS_DIR:$PATH"

install_dotnet_sdk() {
  local install_script
  install_script="$(mktemp)"
  trap 'rm -f "$install_script"' RETURN

  curl --fail --show-error --silent --location "$DOTNET_INSTALL_SCRIPT_URL" --output "$install_script"
  bash "$install_script" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR"
}

install_or_update_global_dotnet_tool() {
  local package_name="$1"
  local package_version="$2"

  if dotnet tool list --global | awk '{ print $1 }' | grep -Fxq "$package_name"; then
    dotnet tool update --global "$package_name" --version "$package_version"
  else
    dotnet tool install --global "$package_name" --version "$package_version"
  fi
}

install_dotnet_sdk
install_or_update_global_dotnet_tool dotnet-ef "$DOTNET_EF_VERSION"
install_or_update_global_dotnet_tool Aspire.Cli "$ASPIRE_CLI_VERSION"

dotnet --version
dotnet ef --version
aspire --version
