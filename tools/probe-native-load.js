'use strict';

// Executes the reviewed SDK initialization in task-only child processes. Does not call device delegates.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
// This diagnostic manifest identifies the reviewed Release build, not an arbitrary current bin directory.
const managedBuildCommit = '172ce59';
const managedHashes = {
    x86: ['75622bfc73dda79620b69dd12796c3ba609e3f9c5efcbcd8e58030569fde4d92', '83952c5a8075b27afd871a91c1c23190801d8ed9e3a995c9d34a1741657024e5'],
    x64: ['5c82b710ea9c5da6f6ab685f2bf5a230ae6670c7b77f658cbcdcd3cb0b0da556', '83952c5a8075b27afd871a91c1c23190801d8ed9e3a995c9d34a1741657024e5']
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
    if (/^NATIVE_LOAD\|status=Failed\|code=NativeModuleConflict\|win32=(none|[0-9]{1,10})\|detail=(preload|postload):(system-import|app-local-system-import|vendor-module):(setupapi\.dll|kernel32\.dll|user32\.dll|gdi32\.dll|msimg32\.dll|winspool\.drv|advapi32\.dll|shell32\.dll|shlwapi\.dll|uxtheme\.dll|ole32\.dll|oleaut32\.dll|oledlg\.dll|crypt32\.dll|ws2_32\.dll|oleacc\.dll|gdiplus\.dll|imm32\.dll|winmm\.dll|blackfishnfclib\.dll|hfmodule\.dll):(system|sideBySide|other)$/.test(value)) return true;
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
        child.on('error', () => {
            if (child.pid && child.exitCode === null) stop('ProbeStartFailed');
            else { clearTimeout(timer); reject(new Error('ProbeStartFailed')); }
        });
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
    const work = path.join(root, '.work');
    noLinks(work);
    const parent = path.join(work, 'native-load');
    if (!fs.existsSync(parent)) directory(parent);
    noLinks(parent);
    const runRoot = path.join(parent, String(Date.now()));
    directory(runRoot);
    // The pinned x64 managed probe queries the OS API in a managed-only directory; caller PATH/SystemRoot are not used.
    const bootstrap = path.join(runRoot, 'host');
    directory(bootstrap);
    const exeName = 'U2BIO.BT003PROLabelPrinting.Validation.exe';
    const sourceExe = path.join(root, 'tests', 'U2BIO.BT003PROLabelPrinting.Validation', 'bin', 'x64', 'Release', exeName);
    noLinks(sourceExe);
    if (digest(sourceExe) !== managedHashes.x64[0]) throw new Error('ManagedBuildMismatch');
    const bootstrapExe = path.join(bootstrap, exeName);
    fs.copyFileSync(sourceExe, bootstrapExe, fs.constants.COPYFILE_EXCL);
    if (digest(bootstrapExe) !== managedHashes.x64[0]) throw new Error('ManagedCopyMismatch');
    const host = spawnSync(bootstrapExe, ['--host-system-directory'], { cwd: bootstrap,
        env: { NoDefaultCurrentDirectoryInExePath: '1' }, windowsHide: true, shell: false,
        stdio: ['ignore', 'pipe', 'pipe'], encoding: 'utf8', timeout: 10000, maxBuffer: 4096 });
    const hostMatch = /^HOST_SYSTEM_DIRECTORY\|([A-Za-z]:\\[^\r\n|]+)\r?\n$/.exec(host.stdout || '');
    if (host.error || host.status !== 0 || host.stderr || !hostMatch) throw new Error('HostPathMismatch');
    const systemDirectory = hostMatch[1];
    noLinks(systemDirectory);
    if (path.basename(systemDirectory).toLowerCase() !== 'system32' ||
        fs.realpathSync(systemDirectory).toLowerCase() !== systemDirectory.toLowerCase()) throw new Error('HostPathMismatch');
    const systemRoot = path.dirname(systemDirectory);
    const environment = { SystemRoot: systemRoot, WINDIR: systemRoot, ComSpec: path.join(systemDirectory, 'cmd.exe'),
        PATH: systemDirectory, NoDefaultCurrentDirectoryInExePath: '1' };
    const taskkill = fs.realpathSync(path.join(systemDirectory, 'taskkill.exe'));
    noLinks(taskkill);
    if (path.dirname(taskkill).toLowerCase() !== systemDirectory.toLowerCase()) throw new Error('HostPathMismatch');
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
