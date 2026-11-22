"""Attach Frida to the running GoXLR App and log all driver-API traffic to a JSONL file.

Usage: python capture.py <seconds> <outfile.jsonl>
"""
import frida, json, sys, time, pathlib

secs = int(sys.argv[1]) if len(sys.argv) > 1 else 20
out = pathlib.Path(sys.argv[2] if len(sys.argv) > 2 else "capture.jsonl")
script_src = (pathlib.Path(__file__).parent / "hook_tusb.js").read_text()

session = frida.attach("GoXLR App.exe")
f = out.open("w", encoding="utf-8")
n = 0

def on_message(msg, data):
    global n
    if msg["type"] == "send":
        p = msg["payload"]
        f.write(json.dumps(p) + "\n"); f.flush()
        n += 1
        if p.get("t") != "vr":
            print(p)
    else:
        print("ERR", msg)

script = session.create_script(script_src)
script.on("message", on_message)
script.load()
print(f"capturing {secs}s -> {out}")
stop = out.parent / "STOP"
stop.unlink(missing_ok=True)
end = time.time() + secs
while time.time() < end and not stop.exists():
    time.sleep(0.5)
script.unload(); session.detach(); f.close()
print("messages:", n)
