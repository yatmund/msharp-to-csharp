#!/bin/bash
# Compares the Platform front end with a local build of Olive.MvcJs in Chromium. Needs:
#   OLIVE_DIST  Olive.MvcJs dist folder (GPL, never committed here)
#   LIBS        node_modules holding jquery, jquery-validation, jquery-validation-unobtrusive, requirejs, bootstrap, popper.js, moment
#   PW          playwright-core folder     CHROME  Chromium executable     TSC  typescript/bin/tsc
cd "$(dirname "$0")"
rm -rf build
node "$TSC" -p tsconfig.json || exit 1
node server.js & pid=$!
sleep 1
node compare.js both "$1"; rc=$?
kill $pid
exit $rc
