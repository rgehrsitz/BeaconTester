# Redis Key Naming Convention Guide

## Overview

This document outlines the standardized key naming convention for the BeaconTester and Beacon ecosystem. A consistent naming approach is critical to prevent errors, improve maintainability, and enhance system understanding.

## Problem Background

The Beacon ecosystem previously used inconsistent naming conventions between different system layers:

- YAML rules defined keys with `input:` and `output:` prefixes
- Redis adapter converted these to `sensors:` and `outputs:` prefixes
- Generated Beacon code expected `input:` and `output:` prefixes

This inconsistency led to runtime errors where keys couldn't be found because the expected prefix format didn't match the actual stored format.

## Domain-Based Naming Convention

We've adopted a comprehensive domain-prefix naming convention that uses four clear domain prefixes throughout the system:

### 1. input: - External inputs/raw sensor data

- Purpose: Identifies values coming into the system from external sources
- Examples: `input:temperature`, `input:humidity`, `input:pressure`
- Usage: Used for raw sensor readings and external data feeds

### 2. output: - Final outputs/alerts/actions

- Purpose: Identifies values that represent final outcomes of rule processing
- Examples: `output:high_temperature_alert`, `output:valve_position`, `output:system_status`
- Usage: Used for actionable results, alerts, notifications, or control signals

### 3. state: - Derived values shared between rules

- Purpose: Identifies intermediate calculated values that may be used by multiple rules
- Examples: `state:avg_temperature`, `state:heat_index`, `state:trend_direction`
- Usage: Used for "virtual sensors" or calculated metrics that flow between rules

### 4. buffer: - Historical/temporal data

- Purpose: Identifies time-series data or historical records
- Examples: `buffer:temperature_history`, `buffer:error_log`, `buffer:event_sequence`
- Usage: Used for data needed for temporal pattern recognition or trend analysis

## Benefits of This Approach

This domain-prefix naming convention offers numerous advantages:

1. **Clear Data Flow**: Creates an intuitive flow model (inputs → state → outputs)
2. **Explicit Dependencies**: Makes rule dependencies more traceable and easier to understand
3. **Testing Clarity**: Simplifies testing by clarifying value domains and expectations
4. **Eliminates Translation**: Removes the need for prefix translation between system components
5. **Intuitive Organization**: Organizes data logically based on its role in the system
6. **Easier Debugging**: Makes it simpler to identify where data is coming from and going to

## Implementation Guidance

To implement this naming convention across the Beacon ecosystem:

1. **Rule Definition**: Use appropriate domain prefixes in YAML rule definitions
   ```yaml
   sensor: input:temperature
   key: output:high_temperature_alert
   ```

2. **Redis Adapter**: Preserve domain prefixes without translation
   ```csharp
   // Use consistent domain prefixes
   public const string INPUT_PREFIX = "input:";
   public const string OUTPUT_PREFIX = "output:";
   public const string STATE_PREFIX = "state:";
   public const string BUFFER_PREFIX = "buffer:";
   ```

3. **Test Scenarios**: Use domain prefixes in test inputs and expectations
   ```json
   {
     "key": "input:temperature",
     "value": 25.0
   }
   ```

4. **Documentation**: Document the convention and educate team members

## Handling Virtual Sensors 

For derived values that serve as both outputs of one rule and inputs to another:

- Use the `state:` prefix to make the dual-purpose nature explicit
- For example, if one rule calculates heat index and another rule uses it:
  ```yaml
  # Rule 1
  actions:
    - set_value:
        key: state:heat_index
        value_expression: '...'

  # Rule 2
  conditions:
    - condition:
        sensor: state:heat_index
        operator: '>'
        value: 90
  ```

## Conclusion

By adopting this consistent domain-prefix naming convention across all components, we can:

1. Reduce errors from mismatched key conventions
2. Create a more intuitive and maintainable system
3. Make testing more straightforward and reliable
4. Improve system documentation and understanding

All BeaconTester components have been updated to support this naming convention, and it's recommended that all Beacon-related projects adopt it for maximum compatibility.