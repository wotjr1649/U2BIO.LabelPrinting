'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn, spawnSync } = require('node:child_process');

if (process.platform === 'win32') process.env.NoDefaultCurrentDirectoryInExePath = '1';
const root = path.resolve(__dirname, '..');
const configuration = process.argv.length === 2 ? 'Release'
    : process.argv.length === 4 && process.argv[2] === '--configuration' ? process.argv[3] : '';
const evidence = { configuration, commands: [], artifacts: [] };
let logRoot;

function run(command, args, label) {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, { cwd: root, windowsHide: true, shell: false });
        let stdout = '', stderr = '', failed = false;
        const stop = reason => {
            if (failed) return;
            failed = true;
            if (child.pid && child.exitCode === null) {
                const kill = spawnSync(path.join(process.env.SystemRoot, 'System32', 'taskkill.exe'),
                    ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, encoding: 'utf8', timeout: 10000 });
                if (kill.error || kill.status !== 0) reason += ' (process tree termination was not confirmed)';
            }
            clearTimeout(timer);
            reject(new Error(reason));
        };
        const timer = setTimeout(() => stop(`${label} exceeded 120 seconds`), 120000);
        child.stdout.on('data', data => { stdout += data; if (stdout.length + stderr.length > 2 * 1024 * 1024) stop(`${label} exceeded its output limit`); });
        child.stderr.on('data', data => { stderr += data; if (stdout.length + stderr.length > 2 * 1024 * 1024) stop(`${label} exceeded its output limit`); });
        child.on('error', error => { clearTimeout(timer); failed = true; reject(new Error(`${label} could not start (${error.code})`)); });
        child.on('close', code => {
            clearTimeout(timer);
            fs.writeFileSync(path.join(logRoot, `${label}.log`), stdout + stderr);
            evidence.commands.push({ command, args, exitCode: code });
            if (failed) return;
            process.stdout.write(stdout);
            if (code !== 0) reject(new Error(`${label} exited ${code}; see task-local build logs`));
            else resolve(stdout);
        });
    });
}

async function main() {
    if (process.platform !== 'win32') throw new Error('This .NET Framework build check requires Windows.');
    if (!['Debug', 'Release'].includes(configuration)) throw new Error('Usage: node tools/verify-build.js [--configuration Debug|Release]');
    const programFiles86 = process.env['ProgramFiles(x86)'];
    if (!programFiles86) throw new Error('Windows Program Files environment is unavailable.');
    logRoot = path.join(root, '.work', 'foundation', `${configuration}-${Date.now()}`);
    fs.mkdirSync(path.dirname(logRoot), { recursive: true });
    fs.mkdirSync(logRoot);
    const targetingPack = path.join(programFiles86, 'Reference Assemblies', 'Microsoft', 'Framework', '.NETFramework', 'v4.6.1');
    if (!fs.existsSync(path.join(targetingPack, 'mscorlib.dll'))) throw new Error('.NET Framework 4.6.1 targeting pack is missing.');
    const vswhere = path.join(programFiles86, 'Microsoft Visual Studio', 'Installer', 'vswhere.exe');
    const found = await run(vswhere, ['-latest', '-products', '*', '-requires', 'Microsoft.Component.MSBuild', '-find', 'MSBuild\\**\\Bin\\MSBuild.exe'], 'discover-msbuild');
    const msbuild = found.trim().split(/\r?\n/)[0];
    const installedRoots = [programFiles86, process.env.ProgramFiles].filter(Boolean).map(p => path.resolve(p).toLowerCase() + path.sep);
    if (!msbuild || path.basename(msbuild).toLowerCase() !== 'msbuild.exe' || !fs.existsSync(msbuild) ||
        !installedRoots.some(p => path.resolve(msbuild).toLowerCase().startsWith(p))) throw new Error('Installed MSBuild was not resolved under Program Files.');
    evidence.msbuildVersion = (await run(msbuild, ['/version', '/nologo'], 'msbuild-version')).trim();
    evidence.compilerVersion = (await run(path.join(path.dirname(msbuild), 'Roslyn', 'csc.exe'), ['/version'], 'compiler-version')).trim();
    evidence.targetingPack = targetingPack;
    evidence.targetingPackMscorlibSha256 = crypto.createHash('sha256').update(fs.readFileSync(path.join(targetingPack, 'mscorlib.dll'))).digest('hex');
    await run(process.execPath, [path.join(root, 'tools', 'verify-repository.js')], 'repository');
    for (const platform of ['x86', 'x64']) {
        const build = await run(msbuild, [path.join(root, 'U2BIO.LabelPrinting.sln'), '/t:Rebuild', '/m:1', '/nr:false', '/nologo', '/v:minimal',
            `/p:Configuration=${configuration}`, `/p:Platform=${platform}`, '/p:UseSharedCompilation=false'], `build-${platform}`);
        for (const setting of [`BUILD_SETTINGS|U2BIO.BT003PROLabelPrinting|v4.6.1|AnyCPU|false`,
            `BUILD_SETTINGS|U2BIO.BT003PROLabelPrinting.Validation|v4.6.1|${platform}|false`])
            if (!build.includes(setting)) throw new Error(`Evaluated project settings missing for ${platform}.`);
        const output = path.join(root, 'tests', 'U2BIO.BT003PROLabelPrinting.Validation', 'bin', platform, configuration);
        await run(path.join(output, 'U2BIO.BT003PROLabelPrinting.Validation.exe'), [`--expect-${platform}`], `validate-${platform}`);
        for (const name of ['U2BIO.BT003PROLabelPrinting.dll', 'U2BIO.BT003PROLabelPrinting.Validation.exe']) {
            const file = path.join(output, name);
            evidence.artifacts.push({ platform, path: path.relative(root, file).replace(/\\/g, '/'), sha256: crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex') });
        }
    }
    evidence.result = 'passed';
    process.stdout.write(`Build checks passed: ${configuration} x86/x64; library AnyCPU; target .NET Framework 4.6.1.\n`);
}

main().catch(error => { evidence.result = 'failed'; process.stderr.write(`${error.message}\n`); process.exitCode = 1; })
    .finally(() => { if (logRoot) fs.writeFileSync(path.join(logRoot, 'evidence.json'), JSON.stringify(evidence, null, 2) + '\n'); });
