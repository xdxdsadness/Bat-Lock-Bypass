# Presets and host lists

## presets.json

Each preset is an object with two fields:

- `name` — caption shown in the GUI dropdown and the console menu;
- `args` — command line passed to the engine. `{BASE}` is replaced with the
  application folder at startup, so paths like `{BASE}\lists\hosts.txt` always
  resolve correctly no matter where the app is unpacked.

To add a preset, append an object to the JSON array; to remove one, delete it.
The GUI and the console read the file at startup, so no rebuild is needed.

## lists/hosts.txt

One domain per line. A domain covers itself and all its subdomains; lines
starting with `^` are treated as regular expressions. The presets apply only
to traffic towards these hosts — everything else passes through untouched.

Add the sites you need to unblock. Keep the list short: every extra host adds
work for the engine.

## Fake-packet templates in bin/

The `--dpi-desync-fake-tls` / `--dpi-desync-fake-http` / `--dpi-desync-fake-quic`
options point to `.bin` files with pre-built TLS / HTTP / QUIC records
(`tls_clienthello_*.bin`, `stun*.bin`, `quic_initial_*.bin`). The presets
reference them from `bin/`, so keep those files in place.
