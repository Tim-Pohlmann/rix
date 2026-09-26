# Loaded by the bats tests that hand rix results to workflow bash. A hand-written fixture that
# restates rix's JSON by hand drifts the moment a field is renamed or added, and the bash reading it
# (jq with a `// ""` fallback) keeps passing on the stale shape - so every fixture is checked
# against the schema that tests/Rix.Tests/ResultSchemaTests.cs keeps in step with the C# types.
#
# Requires python3 with jsonschema (python3-jsonschema on Debian/Ubuntu).

SCHEMAS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../schemas" && pwd)"

# Fails, naming the offending field, unless $2 is a result rix could print according to
# schemas/$1. Validates against the one variant its status selects rather than the whole anyOf,
# whose only complaint on a mismatch would be "not valid under any of the given schemas".
assert_matches_schema() {
  local schema="$1" json="$2"
  python3 - "$SCHEMAS_DIR/$schema" "$json" <<'EOF'
import json, sys
from jsonschema import Draft202012Validator

schema_path, text = sys.argv[1], sys.argv[2]
with open(schema_path) as f:
    schema = json.load(f)
result = json.loads(text)
status = result.get("status") if isinstance(result, dict) else None
variants = [v for v in schema["anyOf"] if v["properties"]["status"]["const"] == status]
if not variants:
    sys.exit(f"{text}\n  status {status!r} is not one {schema_path} allows")
errors = list(Draft202012Validator(variants[0]).iter_errors(result))
if errors:
    sys.exit(f"{text}\n" + "\n".join(f"  {'/'.join(map(str, e.absolute_path)) or '(root)'}: {e.message}" for e in errors))
EOF
  # python's own status, not 0 - it is the verdict.
  return $?
}
