// Frida hook: log every TUSBAUDIO call the GoXLR App makes to the driver API.
// Read-only: logs arguments/buffers, never modifies them.
'use strict';

function hex(ptr, len) {
  if (ptr.isNull() || len <= 0) return '';
  const n = Math.min(len, 1024);
  const bytes = new Uint8Array(ptr.readByteArray(n));
  return Array.from(bytes).map(b => ('0' + b.toString(16)).slice(-2)).join('');
}

function findApi() {
  for (const m of Process.enumerateModules()) {
    if (/goxlr_audioapi/i.test(m.name)) return m;
  }
  return null;
}

function install(mod) {
  send({ t: 'module', name: mod.name, path: mod.path, base: mod.base.toString() });
  const exps = mod.enumerateExports().filter(e => e.type === 'function' && e.name.startsWith('TUSBAUDIO_'));
  const counts = {};
  for (const e of exps) {
    const name = e.name;
    if (name === 'TUSBAUDIO_ClassVendorRequestOut' || name === 'TUSBAUDIO_ClassVendorRequestIn') {
      // Observed layout on this driver build (x86 stdcall):
      // (handle, recipient, requestType, request, value, index, dataLen, dataPtr, bytesTransferred*, timeout)
      Interceptor.attach(e.address, {
        onEnter(a) {
          this.name = name;
          this.recipient = a[1].toInt32() & 0xff;
          this.reqType = a[2].toInt32() & 0xff;
          this.request = a[3].toInt32() & 0xff;
          this.value = a[4].toInt32() & 0xffff;
          this.index = a[5].toInt32() & 0xffff;
          this.len = a[6].toInt32();
          this.data = a[7];
          this.xfer = a[8];
          this.outHex = '';
          if (name.endsWith('Out')) { try { this.outHex = hex(this.data, this.len); } catch (err) { this.outHex = 'ERR:' + err; } }
        },
        onLeave(r) {
          let xfer = this.len;
          try { if (!this.xfer.isNull()) xfer = this.xfer.readU32(); } catch (_) {}
          let data = this.outHex;
          if (this.name.endsWith('In')) { try { data = hex(this.data, xfer); } catch (err) { data = 'ERR:' + err; } }
          send({ t: 'vr', fn: this.name.endsWith('Out') ? 'OUT' : 'IN', rcpt: this.recipient, type: this.reqType,
                 req: this.request, val: this.value, idx: this.index, len: this.len, xfer: xfer, status: r.toInt32() >>> 0,
                 data: data, ts: Date.now() });
        }
      });
    } else {
      Interceptor.attach(e.address, {
        onEnter() { counts[name] = (counts[name] || 0) + 1; if (counts[name] <= 3) send({ t: 'call', fn: name, ts: Date.now() }); }
      });
    }
  }
  send({ t: 'hooked', n: exps.length });
}

const m = findApi();
if (m) install(m);
else send({ t: 'error', msg: 'goxlr_audioapi not loaded in process' });
