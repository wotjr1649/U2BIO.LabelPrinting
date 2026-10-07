'use strict';

// Executes the reviewed SDK initialization in task-only child processes. Does not call device delegates.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
// This diagnostic manifest identifies the reviewed Release build, not an arbitrary current bin directory.
const managedBuildCommit = '4d78742';
const managedHashes = {
    x86: ['5ed2afdaf9bb1d0a187fbf1cf249d9aff8ff7114d9065a8feb7def36b662fd58', 'ac006c07a5519a6e91a819bdcae53f4345951892e57b71a652cdb406999afe57'],
    x64: ['1c6fd662d7dd93c33ea83332a3dc057d8894b7805d6271a1614903a823e9fd97', 'ac006c07a5519a6e91a819bdcae53f4345951892e57b71a652cdb406999afe57']
};
const passReport = 'NATIVE_LOAD|status=Passed|bound=41|deviceDelegatesInvoked=0';
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
        /^NATIVE_LOAD\|status=Failed\|code=(NativeLibraryMissing|NativeArchitectureMismatch|NativeVersionMismatch|NativeModuleConflict|UnsupportedLoaderPlatform|NativeLoadFailed|NativeEntryPointMissing|PrintContractUnverified|ProbeInternalError|ProbeSlotAbandoned|ProbeSlotBusy|ProbeSlotDenied|ProbeReservationFailed|ProbeTimeout|ProbeBindingMismatch|ProbeJobUnavailable)(\|win32=(none|[0-9]{1,10}))?$/.test(value);
}
async function run(executable, stage, environment, taskkill) {
    return new Promise((resolve, reject) => {
        const nonce = crypto.randomBytes(16).toString('hex');
        const child = spawn(executable, ['--probe-native-load', nonce], { cwd: stage, env: environment, windowsHide: true, shell: false, stdio: ['ignore', 'pipe', 'pipe'] });
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
            const suffix = '|nonce=' + nonce;
            const reports = Buffer.concat(chunks).toString('utf8').split(/\r?\n/).filter(r => r.endsWith(suffix))
                .map(r => r.slice(0, -suffix.length)).filter(safeReport);
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
    // Host-specific diagnostic; fail closed on another OS installation instead of trusting caller PATH/SystemRoot.
    const systemRoot = 'C:\\Windows';
    noLinks(systemRoot); noLinks(path.join(systemRoot, 'System32'));
    if (fs.realpathSync(systemRoot).toLowerCase() !== systemRoot.toLowerCase()) throw new Error('HostPathMismatch');
    const environment = { SystemRoot: systemRoot, WINDIR: systemRoot, ComSpec: path.join(systemRoot, 'System32', 'cmd.exe'),
        PATH: path.join(systemRoot, 'System32'), NoDefaultCurrentDirectoryInExePath: '1' };
    const work = path.join(root, '.work');
    noLinks(work);
    const parent = path.join(work, 'native-load');
    if (!fs.existsSync(parent)) directory(parent);
    noLinks(parent);
    const runRoot = path.join(parent, String(Date.now()));
    directory(runRoot);
    const taskkill = fs.realpathSync(path.join(systemRoot, 'System32', 'taskkill.exe'));
    noLinks(taskkill);
    if (path.dirname(taskkill).toLowerCase() !== path.join(systemRoot, 'System32').toLowerCase()) throw new Error('HostPathMismatch');
    const evidence = { managedBuildCommit, configuration: 'Release', results: [], deviceDelegatesInvoked: 0 };
    for (const platform of ['x86', 'x64']) {
        const stage = path.join(runRoot, platform);
        directory(stage);
        const managedNames = ['U2BIO.BT003PROLabelPrinting.Validation.exe', 'U2BIO.BT003PROLabelPrinting.dll'];
        for (let i = 0; i < managedNames.length; i++) {
            const name = managedNames[i];
            const source = path.join(root, 'tests', 'U2BIO.BT003PROLabelPrinting.Validation', 'bin', platform, 'Release', name);
            noLinks(source);
            if (digest(source) !== managedHashes[platform][i]) throw new Error('ManagedBuildMismatch');
            fs.copyFileSync(source, path.join(stage, name), fs.constants.COPYFILE_EXCL);
            if (digest(path.join(stage, name)) !== managedHashes[platform][i]) throw new Error('ManagedCopyMismatch');
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
    if (evidence.results.some(r => r.exitCode !== 0 || r.report !== passReport)) throw new Error('NativeLoadProbeFailed');
}
main().catch(error => {
    const allowed = ['LinkedPathRejected', 'StageAlreadyExists', 'TerminationUnconfirmed', 'ChildExitUnconfirmed', 'ProbeSupervisorTimeout',
        'ProbeOutputLimit', 'ProbeStartFailed', 'ProbeReportInvalid', 'ProbeUsage', 'LocalSdkPathRequired', 'GitUnavailable', 'RevisionInvalid',
        'SdkBaselineMismatch', 'SdkCopyMismatch', 'NativeLoadProbeFailed', 'ManagedBuildMismatch', 'ManagedCopyMismatch', 'HostPathMismatch'];
    process.stderr.write((allowed.includes(error.message) ? error.message : 'ProbeInternalError') + '\n');
    process.exitCode = 1;
});
