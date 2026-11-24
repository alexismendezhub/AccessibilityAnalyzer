// This script sets up HTTPS for the application using the ASP.NET Core HTTPS certificate
const fs = require('fs');
const spawn = require('child_process').spawn;
const path = require('path');

const baseFolder =
  process.env.APPDATA !== undefined && process.env.APPDATA !== ''
    ? `${process.env.APPDATA}/ASP.NET/https`
    : `${process.env.HOME}/.aspnet/https`;

const certificateArg = process.argv.map(arg => arg.match(/--name=(?<value>.+)/i)).filter(Boolean)[0];
const certificateName = certificateArg ? certificateArg.groups.value : process.env.npm_package_name;

if (!certificateName) {
  console.error('Invalid certificate name. Run this script in the context of an npm/yarn script or pass --name=<<app>> explicitly.')
  process.exit(-1);
}

const certFilePath = path.join(baseFolder, `${certificateName}.pem`);
const keyFilePath = path.join(baseFolder, `${certificateName}.key`);

// angular.json reads the certificate from this local, git-ignored folder, so one `ng serve` command works on every OS
// (the per-OS run-script-os dispatcher it replaces triggers Node's DEP0190 warning).
const localFolder = path.join(__dirname, '.aspnetcore-https');

function copyToLocalFolder() {
  fs.mkdirSync(localFolder, { recursive: true });
  fs.copyFileSync(certFilePath, path.join(localFolder, 'localhost.pem'));
  fs.copyFileSync(keyFilePath, path.join(localFolder, 'localhost.key'));
}

if (!fs.existsSync(baseFolder)) {
    fs.mkdirSync(baseFolder, { recursive: true });
}

if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
  spawn('dotnet', [
    'dev-certs',
    'https',
    '--export-path',
    certFilePath,
    '--format',
    'Pem',
    '--no-password',
  ], { stdio: 'inherit', })
  .on('exit', (code) => {
    if (code === 0) copyToLocalFolder();
    process.exit(code);
  });
} else {
  copyToLocalFolder();
}
