<!-- guide: API or CLI reference: exact facts to look up, not a tutorial. Take every name, type,
     default and exit code from the code at the target. Repeat the section from the command heading to
     Examples for each command, endpoint or function. -->

# {{Interface name}} reference

{{One sentence: what this interface is, and which version or branch this reference describes.}}

## {{Command, endpoint or function}}

```text
{{Synopsis, for example: tool export [--format csv|json] PATH}}
```

{{What it does, in one or two sentences.}}

### Parameters

| Name | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| {{--option, as code}} | {{type}} | {{yes or no}} | {{default}} | {{accepted values and effect}} |

### Returns

<!-- guide: For a CLI, list the exit codes; for an API or a function, the response or return value. -->

| {{Exit code, status or return value}} | Meaning |
| --- | --- |
| {{value}} | {{meaning}} |

### Errors

| Error | Cause | Resolution |
| --- | --- | --- |
| {{message or code}} | {{cause}} | {{what to do}} |

### Examples

```bash
{{Example command}}
```

{{Expected output or result.}}
