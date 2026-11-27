#define _WIN32_WINNT 0x0601
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <ole2.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <new>
#include <cstdio>

static const CLSID classId = {
    0xe23fbc65, 0xa90b, 0x4468, {0x8e, 0x6f, 0x2f, 0x3b, 0x52, 0x2f, 0xd9, 0x1b}
};
static HMODULE shellModule;
static LONG activeObjects;

struct FileHandle {
    HANDLE value;
    explicit FileHandle(HANDLE handle) : value(handle) {}
    ~FileHandle() {
        if (value != INVALID_HANDLE_VALUE && value != NULL) CloseHandle(value);
    }
    FileHandle(const FileHandle&) = delete;
    FileHandle& operator=(const FileHandle&) = delete;
};

static HRESULT LastErrorResult() {
    return HRESULT_FROM_WIN32(GetLastError());
}

static HRESULT LaunchRenamer(HANDLE fileList) {
    std::vector<wchar_t> modulePath(32768);
    DWORD length = GetModuleFileNameW(shellModule, modulePath.data(), static_cast<DWORD>(modulePath.size()));
    if (length == 0) return LastErrorResult();
    if (length == modulePath.size()) return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);

    std::wstring executable(modulePath.data());
    executable.resize(executable.find_last_of(L'\\') + 1);
    executable += L"BatchRenamer.exe";

    // Only this handle is inherited by the new process. Other Explorer handles stay private.
    HANDLE inheritableHandle;
    HANDLE self = GetCurrentProcess();
    if (!DuplicateHandle(self, fileList, self, &inheritableHandle, GENERIC_READ, TRUE, 0))
        return LastErrorResult();
    FileHandle childList(inheritableHandle);

    wchar_t handleNumber[32];
    swprintf_s(handleNumber, L"%llu", static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(childList.value)));
    std::wstring command = L"\"" + executable + L"\" --selection-handle " + handleNumber;
    SIZE_T bufferSize = 0;
    InitializeProcThreadAttributeList(NULL, 1, 0, &bufferSize);
    if (bufferSize == 0) return LastErrorResult();
    std::vector<BYTE> buffer(bufferSize);

    STARTUPINFOEXW startup = {};
    startup.StartupInfo.cb = sizeof(startup);
    startup.lpAttributeList = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(buffer.data());
    if (!InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, &bufferSize))
        return LastErrorResult();

    PROCESS_INFORMATION process = {};
    HRESULT result = S_OK;
    if (!UpdateProcThreadAttribute(startup.lpAttributeList, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                                   &childList.value, sizeof(HANDLE), NULL, NULL)) {
        result = LastErrorResult();
    } else if (!CreateProcessW(executable.c_str(), &command[0], NULL, NULL, TRUE, EXTENDED_STARTUPINFO_PRESENT,
                               NULL, NULL, &startup.StartupInfo, &process)) {
        result = LastErrorResult();
    } else {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    }
    DeleteProcThreadAttributeList(startup.lpAttributeList);
    return result;
}

static HRESULT SendPaths(HDROP selection) {
    UINT count = DragQueryFileW(selection, 0xffffffff, NULL, 0);
    if (count == 0) return DV_E_FORMATETC;

    wchar_t tempDirectory[MAX_PATH + 1];
    DWORD length = GetTempPathW(ARRAYSIZE(tempDirectory), tempDirectory);
    if (length == 0 || length >= ARRAYSIZE(tempDirectory)) return HRESULT_FROM_WIN32(ERROR_BAD_PATHNAME);
    GUID fileId;
    HRESULT result = CoCreateGuid(&fileId);
    if (FAILED(result)) return result;
    wchar_t identifier[39];
    StringFromGUID2(fileId, identifier, ARRAYSIZE(identifier));

    // Delete-on-close also cleans up if the launch fails or either process exits early.
    std::wstring listPath = std::wstring(tempDirectory) + L"BatchRenamer-" + identifier + L".tmp";
    FileHandle fileList(CreateFileW(listPath.c_str(), GENERIC_READ | GENERIC_WRITE, 0, NULL, CREATE_NEW,
                                    FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, NULL));
    if (fileList.value == INVALID_HANDLE_VALUE) return LastErrorResult();

    std::wstring paths;
    for (UINT index = 0; index < count; ++index) {
        UINT pathLength = DragQueryFileW(selection, index, NULL, 0);
        if (pathLength == 0) return DV_E_FORMATETC;
        std::vector<wchar_t> path(pathLength + 1);
        if (DragQueryFileW(selection, index, path.data(), pathLength + 1) != pathLength)
            return DV_E_FORMATETC;
        paths.append(path.data(), pathLength);
        paths.push_back(L'\n');
    }

    if (paths.size() > MAXDWORD / sizeof(wchar_t)) return E_OUTOFMEMORY;
    DWORD bytes = static_cast<DWORD>(paths.size() * sizeof(wchar_t));
    DWORD bytesWritten;
    if (!WriteFile(fileList.value, paths.data(), bytes, &bytesWritten, NULL)) return LastErrorResult();
    if (bytesWritten != bytes) return STG_E_WRITEFAULT;
    LARGE_INTEGER beginning = {};
    if (!SetFilePointerEx(fileList.value, beginning, NULL, FILE_BEGIN)) return LastErrorResult();
    return LaunchRenamer(fileList.value);
}

class SelectionTarget : public IDropTarget {
    LONG references;
    static DWORD ChooseEffect(DWORD allowed) {
        if (allowed & DROPEFFECT_COPY) return DROPEFFECT_COPY;
        return allowed & DROPEFFECT_LINK;
    }
public:
    SelectionTarget() : references(1) {
        InterlockedIncrement(&activeObjects);
    }
    ~SelectionTarget() {
        InterlockedDecrement(&activeObjects);
    }
    STDMETHODIMP QueryInterface(REFIID interfaceId, void** object) {
        if (!object) return E_POINTER;
        *object = NULL;
        if (interfaceId != IID_IUnknown && interfaceId != IID_IDropTarget) return E_NOINTERFACE;
        *object = static_cast<IDropTarget*>(this);
        AddRef();
        return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() {
        return InterlockedIncrement(&references);
    }
    STDMETHODIMP_(ULONG) Release() {
        LONG remaining = InterlockedDecrement(&references);
        if (!remaining) delete this;
        return remaining;
    }
    STDMETHODIMP DragEnter(IDataObject*, DWORD, POINTL, DWORD* effect) {
        if (!effect) return E_POINTER;
        *effect = ChooseEffect(*effect);
        return S_OK;
    }
    STDMETHODIMP DragOver(DWORD, POINTL, DWORD* effect) {
        if (!effect) return E_POINTER;
        *effect = ChooseEffect(*effect);
        return S_OK;
    }
    STDMETHODIMP DragLeave() {
        return S_OK;
    }
    STDMETHODIMP Drop(IDataObject* data, DWORD, POINTL, DWORD* effect) {
        if (!data || !effect) return E_POINTER;
        DWORD chosenEffect = ChooseEffect(*effect);
        *effect = DROPEFFECT_NONE;
        FORMATETC format = {CF_HDROP, NULL, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
        STGMEDIUM medium = {};
        HRESULT result = data->GetData(&format, &medium);
        if (FAILED(result)) return result;

        try {
            result = SendPaths(static_cast<HDROP>(medium.hGlobal));
        } catch (const std::bad_alloc&) {
            result = E_OUTOFMEMORY;
        } catch (...) {
            result = E_UNEXPECTED;
        }
        ReleaseStgMedium(&medium);
        if (SUCCEEDED(result)) *effect = chosenEffect;
        return result;
    }
};

class SelectionFactory : public IClassFactory {
    LONG references;
public:
    SelectionFactory() : references(1) {
        InterlockedIncrement(&activeObjects);
    }
    ~SelectionFactory() {
        InterlockedDecrement(&activeObjects);
    }
    STDMETHODIMP QueryInterface(REFIID interfaceId, void** object) {
        if (!object) return E_POINTER;
        *object = NULL;
        if (interfaceId != IID_IUnknown && interfaceId != IID_IClassFactory) return E_NOINTERFACE;
        *object = static_cast<IClassFactory*>(this);
        AddRef();
        return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() {
        return InterlockedIncrement(&references);
    }
    STDMETHODIMP_(ULONG) Release() {
        LONG remaining = InterlockedDecrement(&references);
        if (!remaining) delete this;
        return remaining;
    }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID interfaceId, void** object) {
        if (!object) return E_POINTER;
        *object = NULL;
        if (outer) return CLASS_E_NOAGGREGATION;
        SelectionTarget* target = new(std::nothrow) SelectionTarget();
        if (!target) return E_OUTOFMEMORY;
        HRESULT result = target->QueryInterface(interfaceId, object);
        target->Release();
        return result;
    }
    STDMETHODIMP LockServer(BOOL locked) {
        if (locked) InterlockedIncrement(&activeObjects);
        else InterlockedDecrement(&activeObjects);
        return S_OK;
    }
};

STDAPI DllGetClassObject(REFCLSID requestedClass, REFIID interfaceId, void** object) {
    if (!object) return E_POINTER;
    *object = NULL;
    if (requestedClass != classId) return CLASS_E_CLASSNOTAVAILABLE;
    SelectionFactory* factory = new(std::nothrow) SelectionFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT result = factory->QueryInterface(interfaceId, object);
    factory->Release();
    return result;
}

STDAPI DllCanUnloadNow() {
    return InterlockedCompareExchange(&activeObjects, 0, 0) ? S_FALSE : S_OK;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) shellModule = instance;
    return TRUE;
}
