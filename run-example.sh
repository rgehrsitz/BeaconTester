#!/bin/bash

# Create example directory if it doesn't exist
mkdir -p examples

# Copy an example rule file from Pulsar
cp /home/robertg/Pulsar/Examples/BasicRules/temperature_rules.yaml examples/

# Generate test scenarios from the rules
dotnet run --project BeaconTester.Runner/BeaconTester.Runner.csproj generate --rules examples/temperature_rules.yaml --output examples/test-scenarios.json

echo "Generated test scenarios saved to examples/test-scenarios.json"
echo ""
echo "To run the tests, make sure Redis is running and execute:"
echo "dotnet run --project BeaconTester.Runner/BeaconTester.Runner.csproj run --scenarios examples/test-scenarios.json --output examples/results.json"
echo ""
echo "To generate a report:"
echo "dotnet run --project BeaconTester.Runner/BeaconTester.Runner.csproj report --results examples/results.json --output examples/report.html --format html"