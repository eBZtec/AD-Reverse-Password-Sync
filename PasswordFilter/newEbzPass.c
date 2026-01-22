#define UNICODE
#define _UNICODE
#include <windows.h>
#include <stdio.h>
#include <wchar.h>
#include <stdbool.h>
#include <wincrypt.h>

typedef unsigned char  BOOLEAN;
typedef wchar_t        WCHAR;
typedef WCHAR*         PWSTR;
typedef unsigned short USHORT;

typedef struct _UNICODE_STRING {
    USHORT Length;
    USHORT MaximumLength;
    PWSTR  Buffer;
} UNICODE_STRING, *PUNICODE_STRING;

#ifndef NTSTATUS
typedef long NTSTATUS;
#endif
#ifndef STATUS_SUCCESS
#define STATUS_SUCCESS ((NTSTATUS)0x00000000L)
#endif

//////////// log ////////////
static void WriteLog(const wchar_t* fmt, ...)
{
    HANDLE hFile;
    DWORD written;
    SYSTEMTIME st;
    va_list args;
    wchar_t buf[1200];

    GetLocalTime(&st);
    swprintf_s(buf, 256,
        L"[%04d-%02d-%02d %02d:%02d:%02d] ",
        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);

    va_start(args, fmt);
    _vsnwprintf_s(buf + wcslen(buf), _countof(buf) - wcslen(buf), _TRUNCATE, fmt, args);
    va_end(args);
    wcscat_s(buf, _countof(buf), L"\r\n");

    hFile = CreateFileW(L"C:\\ProgramData\\eBZ Tecnologia\\ebzPassFilter.log",
                        FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS,
                        FILE_ATTRIBUTE_NORMAL, NULL);
    if (hFile == INVALID_HANDLE_VALUE) return;

    SetFilePointer(hFile, 0, NULL, FILE_END);
    WriteFile(hFile, buf, (DWORD)(wcslen(buf) * sizeof(wchar_t)), &written, NULL);
    CloseHandle(hFile);
}

//////////// utils ////////////
static void secure_wipe(void* p, size_t cb) {
    if (p && cb) SecureZeroMemory(p, cb);
}

static void CopyUnicodeString(PUNICODE_STRING us, PWSTR outBuf, size_t outChars)
{
    if (!outBuf || outChars == 0) return;
    outBuf[0] = L'\0';
    if (!us || !us->Buffer) return;
    size_t len = us->Length / sizeof(WCHAR);
    size_t n   = (len < outChars - 1) ? len : (outChars - 1);
    if (n) wcsncpy_s(outBuf, outChars, us->Buffer, n);
}

#pragma comment(lib, "crypt32.lib")
static BOOL DpapiEncryptLocalMachineW(const wchar_t* plaintext, wchar_t** outBase64)
{
    if (!plaintext || !outBase64) return FALSE;
    *outBase64 = NULL;

    DATA_BLOB in = {
        .cbData = (DWORD)((lstrlenW(plaintext) + 1) * sizeof(wchar_t)),
        .pbData = (BYTE*)plaintext
    };

    DATA_BLOB out = { 0 };
    DWORD flags = CRYPTPROTECT_UI_FORBIDDEN | CRYPTPROTECT_LOCAL_MACHINE;

    if (!CryptProtectData(&in, L"", NULL, NULL, NULL, flags, &out))
        return FALSE;

    DWORD cchB64 = 0;
    if (!CryptBinaryToStringW(out.pbData, out.cbData,
                              CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF,
                              NULL, &cchB64)) {
        LocalFree(out.pbData);
        return FALSE;
    }

    wchar_t* b64 = (wchar_t*)LocalAlloc(LMEM_FIXED, cchB64 * sizeof(wchar_t));
    if (!b64) {
        LocalFree(out.pbData);
        SetLastError(ERROR_OUTOFMEMORY);
        return FALSE;
    }

    if (!CryptBinaryToStringW(out.pbData, out.cbData,
                              CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF,
                              b64, &cchB64)) {
        LocalFree(out.pbData);
        LocalFree(b64);
        return FALSE;
    }

    LocalFree(out.pbData);
    *outBase64 = b64;
    return TRUE;
}

//////////// pipe ////////////
static void WriteBytes(HANDLE hPipe, const wchar_t* wmsg)
{
    DWORD bytesToWrite = (DWORD)(lstrlenW(wmsg) * sizeof(wchar_t));
    DWORD written = 0;

    if (!WriteFile(hPipe, &bytesToWrite, sizeof(DWORD), &written, NULL)) return;

    if (bytesToWrite > 0) {
        WriteFile(hPipe, (const BYTE*)wmsg, bytesToWrite, &written, NULL);
    }
}

static BOOL ReadBytes(HANDLE hPipe, int* outValue, DWORD timeoutMs)
{
    OVERLAPPED ov = {0};
    ov.hEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!ov.hEvent) return FALSE;
    DWORD bytesRead = 0;
    BOOL ok = ReadFile(hPipe, outValue, sizeof(int), &bytesRead, &ov);

    if (!ok) {
        DWORD err = GetLastError();
        if (err == ERROR_IO_PENDING) {
            DWORD waitRes = WaitForSingleObject(ov.hEvent, timeoutMs);
            if (waitRes == WAIT_TIMEOUT) {
                CancelIoEx(hPipe, &ov);
                CloseHandle(ov.hEvent);
                SetLastError(WAIT_TIMEOUT);
                return FALSE;
            }
            if (waitRes != WAIT_OBJECT_0) {
                CloseHandle(ov.hEvent);
                return FALSE;
            }
            ok = GetOverlappedResult(hPipe, &ov, &bytesRead, FALSE);
        } else {
            CloseHandle(ov.hEvent);
            return FALSE;
        }
    }

    CloseHandle(ov.hEvent);
    return ok;
}

__declspec(dllexport) BOOL UsePipe(const wchar_t* name, const wchar_t* pass, BOOLEAN setOperation)
{
    const wchar_t* pipename = L"\\\\.\\pipe\\EbzPassFilter";

    if (!WaitNamedPipeW(pipename, 12000)) {
        WriteLog(L"UsePipe: Failed Message -> Pipe server not available...");
        return FALSE;
    }

    HANDLE hPipe = CreateFileW(pipename, GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL);

    if (hPipe == INVALID_HANDLE_VALUE) {
        WriteLog(L"UsePipe: Failed Message -> Can't connect to Pipe Server");
        return FALSE;
    }

    wchar_t json[1024];
    swprintf(json, 1024, L"{\"user\": \"%ls\",\"pass\": \"%ls\", \"set\": %d}", name, pass, setOperation ? 1 : 0);

    wchar_t* b64 = NULL;
    
    if (!DpapiEncryptLocalMachineW(json, &b64) || !b64) {
        WriteLog(L"UsePipe: DPAPI encrypt failed (err=%lu), skipping midPoint validation...", GetLastError());
        CloseHandle(hPipe);
        secure_wipe(json, sizeof(json));
        return TRUE;
    }

    WriteBytes(hPipe, b64);
    FlushFileBuffers(hPipe);

    LocalFree(b64);

    WriteLog(L"UsePipe: Succeeded Message -> Pass update sent to Pipe Server");

    int reply = 0;
    if(!ReadBytes(hPipe, &reply, 10000)){
        WriteLog(L"UsePipe: ReadBytes -> Could not read the response"); 
    }

    CloseHandle(hPipe);

    secure_wipe(json, sizeof(json));

    WriteLog(L"UsePipe: Received response -> %d", reply); 

    if(reply != 1){return FALSE;}    
    return TRUE;
}

//////////// dll ////////////
__declspec(dllexport) BOOLEAN __stdcall InitializeChangeNotify(VOID)
{
    WriteLog(L"InitializeChangeNotify: eBZ PasswordFilter loaded by LSA.");
    return TRUE;
}

__declspec(dllexport) BOOLEAN __stdcall PasswordFilter(PUNICODE_STRING AccountName, PUNICODE_STRING FullName, PUNICODE_STRING Password, BOOLEAN SetOperation)
{
    wchar_t acct[256], name[256], wpass[512];
    CopyUnicodeString(AccountName, acct, _countof(acct));
    CopyUnicodeString(FullName,   name, _countof(name));
    CopyUnicodeString(Password, wpass, _countof(wpass));

    WriteLog(L"PasswordFilter: account='%ls' full='%ls' setOp=%d", acct, name, SetOperation ? 1 : 0);

    if (!UsePipe(acct, wpass, SetOperation))
    {
        WriteLog(L"PasswordFilter: REJECT account='%ls'", acct);
        secure_wipe(acct, sizeof(acct));
        secure_wipe(name, sizeof(name));
        secure_wipe(wpass, sizeof(wpass));
        return FALSE;
    }

    WriteLog(L"PasswordFilter: ACCEPT account='%ls'", acct);
    secure_wipe(acct, sizeof(acct));
    secure_wipe(name, sizeof(name));
    secure_wipe(wpass, sizeof(wpass));
    return TRUE;
}

__declspec(dllexport) NTSTATUS __stdcall PasswordChangeNotify(PUNICODE_STRING UserName, ULONG RelativeId, PUNICODE_STRING NewPassword)
{
    return STATUS_SUCCESS;
}

/////////////////// tests /////////////////////
// void main(){
//     const wchar_t* fakeuser = L"g100";
//     const wchar_t* fakepass = L"lanternaMagica1!";
//
//     if (!UsePipe(fakeuser, fakepass, 1))
 //    {
 //        printf("PasswordFilter: REJECT account='%ls'", fakeuser);
 //        WriteLog(L"PasswordFilter: REJECT account='%ls'", fakeuser);
 //    }
 //    else
 //    {
 //       printf("PasswordFilter: ACCEPT account='%ls'", fakeuser);
 //       WriteLog(L"PasswordFilter: ACCEPT account='%ls'", fakeuser);
 //    }
 //    getchar();
//}