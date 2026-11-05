#!/bin/bash
# Compact mismatch report: one block per failing method.
cd "$(dirname "$0")/../.." || exit 1
dotnet build tests/Platform.Compat.Tests -v q 2>&1 | grep -E " error " | sort -u
dotnet run --project tests/Platform.Compat.Tests --no-build 2>&1 \
  | grep -E "^failed |^  TUnit.Engine.Exceptions.TestFailedException: |^  \(|^  [0-9]+ mismatches|total:|succeeded:|failed:" \
  | grep -v "^  at " | sed -E 's/TUnit.Engine.Exceptions.TestFailedException: \[Test Failure\] AssertionException: //; s/Same_results_as_Olive\(Extensions·//; s/·/./g'
