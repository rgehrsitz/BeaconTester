# BeaconTester

A comprehensive automated testing framework for Beacon solutions, allowing you to validate rule behavior through Redis.

## Overview

BeaconTester is a standalone tool that enables you to:

1. **Generate Test Scenarios**: Automatically analyze rule definitions to create test cases
2. **Run Tests**: Execute tests against a running Beacon instance via Redis
3. **Create Reports**: Generate detailed test reports in various formats

## Project Structure

- **BeaconTester.Core**: Core testing components, Redis integration, and validation logic
- **BeaconTester.RuleAnalyzer**: Rule parsing, analysis, and test generation capabilities
- **BeaconTester.Runner**: Command-line interface for running test scenarios
- **BeaconTester.Templates**: Sample test templates for common scenarios

## Getting Started

### Prerequisites

- .NET SDK 9.0 or higher
- A running Redis instance
- Beacon solution to test

### Installation

Clone the repository and build the solution:

```bash
git clone https://github.com/yourusername/BeaconTester.git
cd BeaconTester
dotnet build
```

### Usage

#### Generate Test Scenarios from Rules

```bash
dotnet run --project BeaconTester.Runner/BeaconTester.Runner.csproj generate --rules /path/to/rules.yaml --output /path/to/tests.json
```

#### Run Test Scenarios

```bash
dotnet run --project BeaconTester.Runner/BeaconTester.Runner.csproj run --scenarios /path/to/tests.json --output /path/to/results.json --redis-host localhost --redis-port 6379
```

#### Generate Reports

```bash
dotnet run --project BeaconTester.Runner/BeaconTester.Runner.csproj report --results /path/to/results.json --output /path/to/report.html --format html
```

## Test Scenarios

Test scenarios are defined in JSON format. Here's a simple example:

```json
{
  "name": "SimpleTemperatureAlertTest",
  "description": "Tests the basic temperature alert rule",
  "steps": [
    {
      "name": "Set temperature below threshold",
      "inputs": [
        {
          "key": "input:temperature",
          "value": 25.0,
          "format": "string"
        }
      ],
      "delay": 500,
      "expectations": [
        {
          "key": "output:high_temperature_alert",
          "expected": false,
          "validator": "boolean"
        }
      ]
    },
    {
      "name": "Set temperature above threshold",
      "inputs": [
        {
          "key": "input:temperature",
          "value": 35.0,
          "format": "string"
        }
      ],
      "delay": 500,
      "expectations": [
        {
          "key": "output:high_temperature_alert",
          "expected": true,
          "validator": "boolean"
        }
      ]
    }
  ]
}
```

## Advanced Features

### Testing Temporal Rules

BeaconTester supports testing temporal conditions by defining sequences of inputs with delays:

```json
{
  "name": "TemperatureRisingPatternTest",
  "description": "Tests detection of rising temperature pattern",
  "inputSequence": [
    { "input:temperature": 20, "delayMs": 500 },
    { "input:temperature": 22, "delayMs": 500 },
    { "input:temperature": 24, "delayMs": 500 },
    { "input:temperature": 26, "delayMs": 500 },
    { "input:temperature": 28, "delayMs": 500 }
  ],
  "expectedOutputs": {
    "output:temperature_rising": true
  }
}
```

### Testing Rule Dependencies

Test dependent rules by pre-setting outputs:

```json
{
  "name": "DependencyTest",
  "description": "Tests rule that depends on another rule's output",
  "preSetOutputs": {
    "output:high_temperature": true
  },
  "inputs": {
    "input:humidity": 70
  },
  "expectedOutputs": {
    "output:heat_alert": true
  }
}
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.