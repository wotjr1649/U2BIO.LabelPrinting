'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn, spawnSync } = require('node:child_process');

if (process.platform === 'win32') process.env.NoDefaultCurrentDirectoryInExePath = '1';
const root = path.resolve(__dirname, '..');
const cli = process.argv.slice(2);
const txtOnly = cli.length >= 2 && cli[cli.length - 2] === '--scope' && cli[cli.length - 1] === 'txt';
if (txtOnly) cli.splice(-2);
const configuration = cli.length === 0 ? 'Release'
    : cli.length === 2 && cli[0] === '--configuration' ? cli[1] : '';
const evidence = { configuration, scope: txtOnly ? 'txt-and-printer' : 'full', deferredFormats: [], commands: [], artifacts: [] };
let logRoot;
let childEnv, taskkill;

function inside(parent, candidate) {
    const relative = path.relative(parent, candidate);
    return relative === '' || (!relative.startsWith('..' + path.sep) && relative !== '..' && !path.isAbsolute(relative));
}

function createTaskDirectory(directory) {
    const realRoot = fs.realpathSync(root);
    if (!inside(realRoot, fs.realpathSync(path.dirname(directory)))) throw new Error('Task directory parent is outside the repository.');
    if (!fs.existsSync(directory)) fs.mkdirSync(directory);
    if (fs.lstatSync(directory).isSymbolicLink() || !inside(realRoot, fs.realpathSync(directory))) throw new Error('Task directory redirects outside its owned location.');
}

function decodeOutput(chunks) {
    const bytes = Buffer.concat(chunks);
    try { return new TextDecoder('utf-8', { fatal: true }).decode(bytes); }
    catch { return new TextDecoder('euc-kr', { fatal: true }).decode(bytes); }
}

function run(command, args, label) {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, { cwd: root, windowsHide: true, shell: false, env: childEnv });
        const stdoutChunks = [], stderrChunks = [];
        let outputBytes = 0, failureReason = '', cleanupTimer;
        const stop = reason => {
            if (failureReason) return;
            failureReason = reason;
            if (child.pid && child.exitCode === null) {
                const kill = spawnSync(taskkill, ['/PID', String(child.pid), '/T', '/F'],
                    { windowsHide: true, encoding: 'utf8', timeout: 10000, env: childEnv, maxBuffer: 128 * 1024 });
                if (kill.error || kill.status !== 0) failureReason += ' (process tree termination was not confirmed)';
            }
            clearTimeout(timer);
            cleanupTimer = setTimeout(() => reject(new Error(failureReason + ' (child exit was not confirmed)')), 5000);
        };
        const timer = setTimeout(() => stop(`${label} exceeded 120 seconds`), 120000);
        const collect = (chunks, data) => {
            outputBytes += data.length;
            if (outputBytes > 2 * 1024 * 1024) stop(`${label} exceeded its output limit`);
            else chunks.push(data);
        };
        child.stdout.on('data', data => collect(stdoutChunks, data));
        child.stderr.on('data', data => collect(stderrChunks, data));
        child.on('error', error => { clearTimeout(timer); failureReason = `${label} could not start (${error.code})`; reject(new Error(failureReason)); });
        child.on('close', code => {
            clearTimeout(timer);
            clearTimeout(cleanupTimer);
            let stdout, stderr;
            try { stdout = decodeOutput(stdoutChunks); stderr = decodeOutput(stderrChunks); }
            catch { reject(new Error(`${label} output encoding is unsupported`)); return; }
            fs.writeFileSync(path.join(logRoot, `${label}.log`), stdout + stderr);
            evidence.commands.push({ command, args, exitCode: code });
            if (failureReason) { reject(new Error(failureReason)); return; }
            if (stdout) process.stdout.write(stdout.endsWith('\n') ? stdout : stdout + '\n');
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
    const work = path.join(root, '.work');
    const foundation = path.join(work, 'foundation');
    createTaskDirectory(work);
    createTaskDirectory(foundation);
    const proposedLogRoot = path.join(foundation, `${configuration}-${Date.now()}`);
    if (fs.existsSync(proposedLogRoot)) throw new Error('Task log directory already exists.');
    createTaskDirectory(proposedLogRoot);
    logRoot = proposedLogRoot;
    const temporary = path.join(logRoot, 'tmp');
    createTaskDirectory(temporary);
    childEnv = {};
    for (const key of ['PATH', 'SystemRoot', 'WINDIR', 'ComSpec', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432',
        'PROCESSOR_ARCHITECTURE', 'PROCESSOR_ARCHITEW6432', 'NUMBER_OF_PROCESSORS', 'DEVFLOW_ROOT'])
        if (process.env[key]) childEnv[key] = process.env[key];
    childEnv.TEMP = temporary;
    childEnv.TMP = temporary;
    childEnv.NoDefaultCurrentDirectoryInExePath = '1';
    if (!process.env.SystemRoot || !path.isAbsolute(process.env.SystemRoot)) throw new Error('Windows system root is unavailable.');
    const realSystemRoot = fs.realpathSync(process.env.SystemRoot);
    taskkill = fs.realpathSync(path.join(realSystemRoot, 'System32', 'taskkill.exe'));
    if (!inside(realSystemRoot, taskkill)) throw new Error('Process cleanup executable is outside the Windows directory.');
    const installedRoots = [programFiles86, process.env.ProgramFiles].filter(Boolean).map(p => fs.realpathSync(p));
    const toolRoots = installedRoots.concat(realSystemRoot);
    const toolDirectories = (process.env.PATH || '').split(path.delimiter).filter(Boolean).flatMap(p => {
        try { const actual = fs.realpathSync(p.replace(/^"|"$/g, '')); return toolRoots.some(r => inside(r, actual)) ? [actual] : []; }
        catch { return []; }
    });
    childEnv.PATH = toolDirectories.join(path.delimiter);
    const gitCandidate = toolDirectories.map(p => path.join(p, 'git.exe')).find(p => fs.existsSync(p));
    if (!gitCandidate) throw new Error('Git was not found under installed tool directories.');
    const git = fs.realpathSync(gitCandidate);
    if (!toolRoots.some(p => inside(p, git))) throw new Error('Git resolves outside installed tool directories.');
    evidence.gitCommit = (await run(git, ['rev-parse', 'HEAD'], 'revision')).trim();
    evidence.workingTreeDirty = (await run(git, ['status', '--porcelain'], 'working-tree')).trim().length !== 0;
    const targetingPack = path.join(programFiles86, 'Reference Assemblies', 'Microsoft', 'Framework', '.NETFramework', 'v4.6.1');
    if (!fs.existsSync(path.join(targetingPack, 'mscorlib.dll'))) throw new Error('.NET Framework 4.6.1 targeting pack is missing.');
    const vswhere = fs.realpathSync(path.join(programFiles86, 'Microsoft Visual Studio', 'Installer', 'vswhere.exe'));
    if (!installedRoots.some(p => inside(p, vswhere))) throw new Error('Visual Studio discovery executable resolves outside Program Files.');
    const found = await run(vswhere, ['-latest', '-products', '*', '-requires', 'Microsoft.Component.MSBuild', '-find', 'MSBuild\\**\\Bin\\MSBuild.exe'], 'discover-msbuild');
    const discovered = found.trim().split(/\r?\n/)[0];
    if (!discovered || path.basename(discovered).toLowerCase() !== 'msbuild.exe' || !fs.existsSync(discovered)) throw new Error('Installed MSBuild was not resolved.');
    const msbuild = fs.realpathSync(discovered);
    if (!installedRoots.some(p => inside(p, msbuild))) throw new Error('Installed MSBuild resolves outside Program Files.');
    const compiler = fs.realpathSync(path.join(path.dirname(msbuild), 'Roslyn', 'csc.exe'));
    if (!installedRoots.some(p => inside(p, compiler))) throw new Error('Compiler resolves outside Program Files.');
    evidence.msbuildVersion = (await run(msbuild, ['/version', '/nologo'], 'msbuild-version')).trim();
    evidence.compilerVersion = (await run(compiler, ['/version'], 'compiler-version')).trim();
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
        await run(path.join(output, 'U2BIO.BT003PROLabelPrinting.Validation.exe'), [`--expect-${platform}`, ...(txtOnly ? ['--txt-only'] : [])], `validate-${platform}`);
        const harness = path.join(root, 'tests', 'U2BIO.BT003PROLabelPrinting.SdkHarness');
        await run(msbuild, [path.join(harness, 'U2BIO.BT003PROLabelPrinting.SdkHarness.csproj'), '/t:Rebuild', '/m:1', '/nr:false', '/nologo', '/v:minimal',
            `/p:Configuration=${configuration}`, `/p:Platform=${platform}`], `harness-build-${platform}`);
        await run(path.join(harness, 'bin', platform, configuration, 'U2BIO.BT003PROLabelPrinting.SdkHarness.exe'), [], `harness-${platform}`);
        const example = path.join(root, 'examples', 'OfflineSmoke');
        await run(msbuild, [path.join(example, 'OfflineSmoke.csproj'), '/t:Rebuild', '/m:1', '/nr:false', '/nologo', '/v:minimal',
            `/p:Configuration=${configuration}`, `/p:Platform=${platform}`], `example-build-${platform}`);
        await run(path.join(example, 'bin', platform, configuration, 'U2BIO.LabelPrinting.OfflineSmoke.exe'), [`--expect-${platform}`], `example-${platform}`);
        // 실제 배포 속성을 사용해 복사된 SDK의 PE·고정 해시를 검사한다. DLL 로딩은 하지 않는다.
        const product = path.join(root, 'src', 'U2BIO.BT003PROLabelPrinting');
        await run(msbuild, [path.join(product, 'U2BIO.BT003PROLabelPrinting.csproj'), '/t:Rebuild', '/m:1', '/nr:false', '/nologo', '/v:minimal',
            `/p:Configuration=${configuration}`, `/p:Bt003NativePlatform=${platform}`], `deployment-build-${platform}`);
        const deployment = path.join(product, 'bin', platform, configuration);
        const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
        if (hash(path.join(deployment, 'U2BIO.BT003PROLabelPrinting.dll')) !== hash(path.join(output, 'U2BIO.BT003PROLabelPrinting.dll')))
            throw new Error(`Deployed managed DLL differs from validated product (${platform}).`);
        await run(path.join(output, 'U2BIO.BT003PROLabelPrinting.Validation.exe'), ['--inspect-deployment', platform, deployment], `deployment-inspect-${platform}`);
        for (const name of ['BlackfishNfcLib.dll', 'HFModule.dll']) {
            const file = path.join(deployment, name);
            evidence.artifacts.push({ platform, path: path.relative(root, file).replace(/\\/g, '/'),
                sha256: crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex') });
        }
        for (const name of ['U2BIO.BT003PROLabelPrinting.dll', 'U2BIO.BT003PROLabelPrinting.Validation.exe']) {
            const file = path.join(output, name);
            const snapshotFolder = path.join(logRoot, platform);
            if (!fs.existsSync(snapshotFolder)) createTaskDirectory(snapshotFolder);
            const snapshot = path.join(snapshotFolder, name);
            fs.copyFileSync(file, snapshot, fs.constants.COPYFILE_EXCL);
            evidence.artifacts.push({ platform, path: path.relative(root, file).replace(/\\/g, '/'),
                snapshot: path.relative(root, snapshot).replace(/\\/g, '/'), sha256: crypto.createHash('sha256').update(fs.readFileSync(snapshot)).digest('hex') });
        }
    }
    evidence.result = 'passed';
    process.stdout.write(`Build checks passed: ${configuration} x86/x64; library AnyCPU; target .NET Framework 4.6.1.\n`);
}

main().catch(error => { evidence.result = 'failed'; process.stderr.write(`${error.message}\n`); process.exitCode = 1; })
    .finally(() => { if (logRoot) fs.writeFileSync(path.join(logRoot, 'evidence.json'), JSON.stringify(evidence, null, 2) + '\n'); });
