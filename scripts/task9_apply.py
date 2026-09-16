from pathlib import Path
import textwrap

workflow = Path('.github/workflows/task9-tdd.yml').read_text(encoding='utf-8')
start_token = "          python - <<'PY'\n"
end_token = "\n          PY\n"
start = workflow.index(start_token) + len(start_token)
end = workflow.index(end_token, start)
original_script = textwrap.dedent(workflow[start:end])
exec(compile(original_script, 'task9-apply', 'exec'))

path = Path('src/backend/NexoMail.ControlCenterSmokeTests/Program.cs')
text = path.read_text(encoding='utf-8')
fault_marker = 'sealed class FaultIsolationSyncHttpHandler'
fault_start = text.index(fault_marker)
prefix, fault = text[:fault_start], text[fault_start:]

if 'fixture-history-901' not in fault:
    needle = '''        if (uri.Contains("/users/me/messages?", StringComparison.OrdinalIgnoreCase))
            return Json(HttpStatusCode.OK, "{\\"messages\\":[]}");'''
    replacement = '''        if (uri.Contains("/users/me/profile", StringComparison.OrdinalIgnoreCase))
            return Json(HttpStatusCode.OK, "{\\"historyId\\":\\"fixture-history-900\\"}");

        if (uri.Contains("/users/me/history?", StringComparison.OrdinalIgnoreCase))
            return Json(HttpStatusCode.OK, "{\\"history\\":[],\\"historyId\\":\\"fixture-history-901\\"}");

''' + needle
    if needle not in fault:
        raise SystemExit('No se encontró el punto de inserción del fixture Gmail sano.')
    fault = fault.replace(needle, replacement, 1)
    path.write_text(prefix + fault, encoding='utf-8')
