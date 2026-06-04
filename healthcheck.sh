#!/bin/sh
# healthcheck.sh

# Exit immediately if a command exits with a non-zero status
set -e

# Check if the primary gateway port is responding
# Adjust the /health endpoint to match your actual ASP.NET Core health check route
curl --fail --silent http://localhost:8080/health || exit 1

exit 0