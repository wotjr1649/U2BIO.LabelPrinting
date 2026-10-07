'use strict';

// Executes the reviewed SDK initialization in task-only child processes. Does not call device delegates.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn, spawnSync, execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const files = ['BlackfishNfcLib.dll', 'HFModule.dll'];
const hashes = {
    x86: ['fed09bae47835dcf78f58579e3cfb9382561799b369e54fd9fa0902e5b89dbf5', 'bb2721801bc853ead75e647608c6b36a5a3a75f344095e4e29ffc35e822319d4'],
    x64: ['64c7874980cb0a774a766e299359bc9cba5dd66fd052d758f1d4d1678d8cf72c', '542ebfbccca1f953b4dcafb2a1faed198707d8fbcdb0653c0fc121d3c273edf5']
};
const digest = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
function noLinks(file) {
    for (let current = path.resolve(file); ; current = path.dirname(current)) {
        if (fs.lstatSync(current).isSymbolicLink()) throw new Error('LinkedPathRejected');
        if (path.dirname(current) === current) break;
    }
}
function directory(file) {
    noLinks(path.dirname(file));
    if (fs.existsSync(file)) throw new Error('StageAlreadyExists');
    fs.mkdirSync(file);
    noLinks(file);
}
function safeReport(value) {
    return /^NATIVE_LOAD\|status=Passed\|bound=41\|deviceDelegatesInvoked=0$/.test(value) ||
        /^NATIVE_LOAD\|status=Failed\|code=(NativeLibraryMissing|NativeArchitectureMismatch|NativeVersionMismatch|NativeModuleConflict|UnsupportedLoaderPlatform|NativeLoadFailed|NativeEntryPointMissing|PrintContractUnverified|ProbeInternalError|ProbeSlotAbandoned|ProbeSlotBusy|ProbeSlotDenied|ProbeReservationFailed|ProbeTimeout|ProbeBindingMismatch)(\|win32=(none|[0-9]{1,10}))?$/.test(value);
}
async function run(executable, stage, environment, taskkill) {
    return new Promise((resolve, reject) => {
        const child = spawn(executable, ['--probe-native-load'], { cwd: stage, env: environment, windowsHide: true, shell: false, stdio: ['ignore', 'pipe', 'pipe'] });
        const chunks = [];
        let bytes = 0, failure = '', cleanup;
        const stop = reason => {
            if (failure) return;
            failure = reason;
            if (child.pid && child.exitCode === null) {
                const killed = spawnSync(taskkill, ['/PID', String(child.pid), '/T', '/F'], { env: environment, windowsHide: true, timeout: 10000, maxBuffer: 65536, stdio: 'ignore' });
                if (killed.error || killed.status !== 0) failure = 'TerminationUnconfirmed';
            }
            clearTimeout(timer);
            cleanup = setTimeout(() => reject(new Error('ChildExitUnconfirmed')), 5000);
        };
        const timer = setTimeout(() => stop('ProbeSupervisorTimeout'), 45000);
        const collect = data => {
            bytes += data.length;
            if (bytes > 128 * 1024) stop('ProbeOutputLimit');
            else chunks.push(data);
        };
        child.stdout.on('data', collect);
        child.stderr.on('data', collect);
        child.on('error', () => { clearTimeout(timer); reject(new Error('ProbeStartFailed')); });
        child.on('close', code => {
            clearTimeout(timer); clearTimeout(cleanup);
            if (failure) { reject(new Error(failure)); return; }
            // Never log raw vendor stdout/stderr; only the bounded probe's fixed safe fields leave this process.
            const reports = Buffer.concat(chunks).toString('utf8').split(/\r?\n/).filter(safeReport);
            if (reports.length !== 1) { reject(new Error('ProbeReportInvalid')); return; }
            resolve({ exitCode: code, report: reports[0] });
        });
    });
}
async function main() {
    if (process.platform !== 'win32' || process.argv.length !== 4 || process.argv[2] !== '--sdk-directory') throw new Error('ProbeUsage');
    const sdk = path.resolve(process.argv[3]);
    if (!/^[A-Za-z]:\\/.test(sdk)) throw new Error('LocalSdkPathRequired');
    noLinks(root); noLinks(sdk);
    const environment = {};
    for (const key of ['SystemRoot', 'WINDIR', 'ComSpec', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432', 'PROCESSOR_ARCHITECTURE', 'PROCESSOR_ARCHITEW6432', 'NUMBER_OF_PROCESSORS'])
        if (process.env[key]) environment[key] = process.env[key];
    environment.NoDefaultCurrentDirectoryInExePath = '1';
    const trusted = [process.env.SystemRoot, process.env.ProgramFiles, process.env['ProgramFiles(x86)']].filter(Boolean).map(p => fs.realpathSync(p));
    environment.PATH = (process.env.PATH || '').split(path.delimiter).filter(Boolean).flatMap(p => {
        try { const actual = fs.realpathSync(p.replace(/^"|"$/g, '')); return trusted.some(t => actual === t || actual.startsWith(t + path.sep)) ? [actual] : []; }
        catch { return []; }
    }).join(path.delimiter);
    const git = environment.PATH.split(path.delimiter).map(p => path.join(p, 'git.exe')).find(p => fs.existsSync(p));
    if (!git) throw new Error('GitUnavailable');
    const revision = execFileSync(fs.realpathSync(git), ['rev-parse', 'HEAD'], { cwd: root, env: environment, windowsHide: true, timeout: 10000, maxBuffer: 4096, encoding: 'utf8' }).trim();
    if (!/^[a-f0-9]{40}$/.test(revision)) throw new Error('RevisionInvalid');
    const work = path.join(root, '.work');
    noLinks(work);
    const parent = path.join(work, 'native-load');
    if (!fs.existsSync(parent)) directory(parent);
    noLinks(parent);
    const runRoot = path.join(parent, String(Date.now()));
    directory(runRoot);
    const taskkill = fs.realpathSync(path.join(process.env.SystemRoot, 'System32', 'taskkill.exe'));
    const evidence = { gitCommit: revision, configuration: 'Release', results: [], deviceDelegatesInvoked: 0 };
    for (const platform of ['x86', 'x64']) {
        const stage = path.join(runRoot, platform);
        directory(stage);
        for (const name of ['U2BIO.BT003PROLabelPrinting.Validation.exe', 'U2BIO.BT003PROLabelPrinting.dll']) {
            const source = path.join(root, 'tests', 'U2BIO.BT003PROLabelPrinting.Validation', 'bin', platform, 'Release', name);
            noLinks(source);
            fs.copyFileSync(source, path.join(stage, name), fs.constants.COPYFILE_EXCL);
        }
        for (let i = 0; i < files.length; i++) {
            const source = path.join(sdk, platform, 'lib', files[i]);
            noLinks(source);
            if (digest(source) !== hashes[platform][i]) throw new Error('SdkBaselineMismatch');
            const target = path.join(stage, files[i]);
            fs.copyFileSync(source, target, fs.constants.COPYFILE_EXCL);
            if (digest(target) !== hashes[platform][i]) throw new Error('SdkCopyMismatch');
        }
        directory(path.join(stage, 'tmp'));
        const result = await run(path.join(stage, 'U2BIO.BT003PROLabelPrinting.Validation.exe'), stage,
            { ...environment, TEMP: path.join(stage, 'tmp'), TMP: path.join(stage, 'tmp') }, taskkill);
        evidence.results.push({ platform, ...result, managedDllSha256: digest(path.join(stage, 'U2BIO.BT003PROLabelPrinting.dll')), managedExeSha256: digest(path.join(stage, 'U2BIO.BT003PROLabelPrinting.Validation.exe')) });
        process.stdout.write(`${platform}: ${result.report}; exit=${result.exitCode}\n`);
    }
    fs.writeFileSync(path.join(runRoot, 'evidence.json'), JSON.stringify(evidence, null, 2) + '\n', { flag: 'wx' });
    if (evidence.results.some(r => r.exitCode !== 0)) throw new Error('NativeLoadProbeFailed');
}
main().catch(error => {
    const allowed = ['LinkedPathRejected', 'StageAlreadyExists', 'TerminationUnconfirmed', 'ChildExitUnconfirmed', 'ProbeSupervisorTimeout',
        'ProbeOutputLimit', 'ProbeStartFailed', 'ProbeReportInvalid', 'ProbeUsage', 'LocalSdkPathRequired', 'GitUnavailable', 'RevisionInvalid',
        'SdkBaselineMismatch', 'SdkCopyMismatch', 'NativeLoadProbeFailed'];
    process.stderr.write((allowed.includes(error.message) ? error.message : 'ProbeInternalError') + '\n');
    process.exitCode = 1;
});
