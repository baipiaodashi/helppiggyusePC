# 小猪电脑助手 CI 自检用的假 AI 接口：第一轮让 AI 调用 pwsh/bash 工具，第二轮回答 All done!
import json, sys, time
from http.server import BaseHTTPRequestHandler, HTTPServer
calls = {"n": 0}
class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers.get('Content-Length', 0))) or b'{}')
        calls["n"] += 1
        tools = [t.get('function', {}).get('name') for t in body.get('tools', [])]
        with open('fake_llm.log', 'a') as f:
            f.write(json.dumps({"path": self.path, "n": calls["n"], "auth": self.headers.get('Authorization','')[:12], "tools": tools,
                                "last": body.get('messages', [])[-1] if body.get('messages') else None}, ensure_ascii=False)[:3000] + "\n")
        self.send_response(200); self.send_header('Content-Type', 'text/event-stream'); self.end_headers()
        def ev(obj): self.wfile.write(("data: " + json.dumps(obj) + "\n\n").encode()); self.wfile.flush()
        last = body.get('messages', [{}])[-1]
        shell = next((t for t in tools if t in ('pwsh', 'bash')), None)
        if last.get('role') == 'user' and shell:
            # 引擎会在用户消息后面再附一条 system-reminder，所以看这一轮所有的用户消息
            text = json.dumps([m for m in body.get('messages', []) if m.get('role') == 'user'], ensure_ascii=False)
            danger = 'PIGGY_DANGER' in text
            sandbox = 'PIGGY_SANDBOX' in text
            if shell == 'pwsh':
                if danger: cmd = "Remove-Item C:\\Windows\\System32\\drivers\\etc\\hosts -Force"
                elif sandbox: cmd = "Set-Content -Path \"$env:USERPROFILE\\piggy-sandbox-probe.txt\" -Value hi; Write-Output wrote-outside-workspace"
                else: cmd = "Write-Output hello-from-harness; $PSVersionTable.PSVersion.ToString()"
            else:
                if danger: cmd = "rm -rf /etc/hosts"
                elif sandbox: cmd = "echo hi > \"$HOME/piggy-sandbox-probe.txt\" && echo wrote-outside-workspace"
                else: cmd = "echo hello-from-harness && uname -a"
            args = json.dumps({"command": cmd, "description": "delete hosts" if danger else ("write a file in the home folder" if sandbox else "print hello")})
            ev({"id": "x", "object": "chat.completion.chunk", "model": "fake-model", "choices": [{"index": 0, "delta": {"role": "assistant", "content": None, "tool_calls": [{"index": 0, "id": "call_1", "type": "function", "function": {"name": shell, "arguments": args}}]}, "finish_reason": None}]})
            ev({"id": "x", "object": "chat.completion.chunk", "model": "fake-model", "choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]})
        else:
            ev({"id": "x", "object": "chat.completion.chunk", "model": "fake-model", "choices": [{"index": 0, "delta": {"role": "assistant", "content": "All done!"}, "finish_reason": None}]})
            ev({"id": "x", "object": "chat.completion.chunk", "model": "fake-model", "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}], "usage": {"prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2}})
        self.wfile.write(b"data: [DONE]\n\n"); self.wfile.flush()
# 用来在 CI 里测试小猪和 Harness 的对接：假装是一个 OpenAI 兼容的 AI 接口
port = int(sys.argv[1]) if len(sys.argv) > 1 else 18080
HTTPServer(('127.0.0.1', port), H).serve_forever()
