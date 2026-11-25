# Demo profile pictures

300 profile pictures for the demo data loader: `avatar-001.jpg` goes to the first demo person,
up to `avatar-300.jpg` (see `DemoData:AvatarsPath` in
[docs/development/configuration.md](../../docs/development/configuration.md)). In Development the
API reads them from this folder; elsewhere set `DemoData:AvatarsPath` to a folder that contains them.

They are part of Knock Twice, provided by the project owner, and distributed under
the repository's [MIT license](../../LICENSE). They are demo material only: never needed in
production, and loading demo data is meant for test installations.
