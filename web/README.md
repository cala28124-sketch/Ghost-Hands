# web

A lesson server and simple webpage that run on the laptop, for moving lessons between the laptop and the headsets.

- The page lets you upload, preview, and pick lessons. Headsets fetch lessons over the local network.
- Storage stays local: a folder of JSON files or SQLite. The plan rules out cloud databases and user accounts.
- Check uploads against the schema in `shared/`.
- This is a should-ship feature. The bundled lesson in Unity is always the fallback, and a judge should never wait on the network.
