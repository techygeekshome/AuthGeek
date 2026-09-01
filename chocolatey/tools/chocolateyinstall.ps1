$ErrorActionPreference = 'Stop'

# AuthGeek ships an Inno Setup installer. The package downloads it from the GitHub release for the
# matching tag and verifies it against a SHA-256 checksum rather than embedding the binary. Because
# nothing is embedded, this package must NOT contain a tools\VERIFICATION.txt - that file is only
# for packages that ship a binary inside the nupkg, and including one is what the USP 8.0.0
# submission was rejected for.
$packageArgs = @{
  packageName    = 'authgeek'
  fileType       = 'exe'
  url            = 'https://github.com/techygeekshome/AuthGeek/releases/download/v1.0.0/AuthGeekSetup.exe'
  checksum       = '004e1ba38c37bf60e6cc65396511ebcae0d554bccb4be9eec78762af1f9be302'
  checksumType   = 'sha256'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
