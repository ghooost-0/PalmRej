#include <ntddk.h>
#include <wdf.h>
#include <usb.h>
#include <usbioctl.h>

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD PalmRawTapEvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL PalmRawTapEvtIoInternalDeviceControl;
EVT_WDF_REQUEST_COMPLETION_ROUTINE PalmRawTapInUrbCompletion;

static volatile LONG64 g_PalmRawTapInCompleteSequence = 0;

/* A460 shadow-only producer bridge. No USB/request behavior changes. */
#define PALM_A460_CALLBACK_NAME L"\\Callback\\PalmRejRawUsbBridgeV1"
#define PALM_A460_BRIDGE_MAGIC 0x30363441UL
#define PALM_A460_BRIDGE_VERSION 1UL

typedef struct _PALM_A460_BRIDGE_FRAME {
    ULONG Magic;
    ULONG Size;
    ULONG Version;
    ULONG Reserved;
    LONG64 BridgeSequence;
    LONG64 UsbSequence;
    ULONGLONG AtUs;
    USHORT ScanTime;
    UCHAR ContactCount;
    UCHAR ReportId;
    UCHAR Raw[64];
} PALM_A460_BRIDGE_FRAME, *PPALM_A460_BRIDGE_FRAME;

EVT_WDF_OBJECT_CONTEXT_CLEANUP PalmA460RawTapDriverCleanup;

static PCALLBACK_OBJECT g_A460RawTapCallbackObject = NULL;
static volatile LONG64 g_A460RawTapBridgeSequence = 0;
static volatile LONG g_A460RawTapNotifyEnabled = 0;

static
NTSTATUS
PalmA460RawTapCreateOrOpenCallback(
    VOID
    )
{
    UNICODE_STRING name;
    OBJECT_ATTRIBUTES attributes;
    NTSTATUS status;

    RtlInitUnicodeString(&name, PALM_A460_CALLBACK_NAME);
    InitializeObjectAttributes(
        &attributes,
        &name,
        OBJ_CASE_INSENSITIVE | OBJ_PERMANENT,
        NULL,
        NULL
        );

    status = ExCreateCallback(
        &g_A460RawTapCallbackObject,
        &attributes,
        TRUE,
        TRUE
        );
    if (NT_SUCCESS(status)) {
        InterlockedExchange(&g_A460RawTapNotifyEnabled, 1);
    }
    else {
        g_A460RawTapCallbackObject = NULL;
    }
    return status;
}

static
VOID
PalmA460RawTapShutdownBridge(
    VOID
    )
{
    InterlockedExchange(&g_A460RawTapNotifyEnabled, 0);
    if (g_A460RawTapCallbackObject != NULL) {
        ObDereferenceObject(g_A460RawTapCallbackObject);
        g_A460RawTapCallbackObject = NULL;
    }
}

VOID
PalmA460RawTapDriverCleanup(
    _In_ WDFOBJECT DriverObject
    )
{
    UNREFERENCED_PARAMETER(DriverObject);
    PalmA460RawTapShutdownBridge();
    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_ERROR_LEVEL,
        "PalmRawUsbTap V1.3: BRIDGE-CLEANUP notify=0 action=CLEANUP-ONLY\n"
        );
}

static
PUCHAR
PalmRawTapGetTransferBuffer(
    _In_ PURB Urb
    )
{
    PVOID buffer;

    if (Urb == NULL) {
        return NULL;
    }

    buffer = Urb->UrbBulkOrInterruptTransfer.TransferBuffer;
    if (buffer != NULL) {
        return (PUCHAR)buffer;
    }

    if (Urb->UrbBulkOrInterruptTransfer.TransferBufferMDL != NULL) {
        buffer = MmGetSystemAddressForMdlSafe(
            Urb->UrbBulkOrInterruptTransfer.TransferBufferMDL,
            NormalPagePriority
            );

        if (buffer != NULL) {
            return (PUCHAR)buffer;
        }
    }

    return NULL;
}

static
BOOLEAN
PalmRawTapIsInboundBulkOrInterruptUrb(
    _In_opt_ PURB Urb
    )
{
    if (Urb == NULL) {
        return FALSE;
    }

    if (Urb->UrbHeader.Function != URB_FUNCTION_BULK_OR_INTERRUPT_TRANSFER) {
        return FALSE;
    }

    if ((Urb->UrbBulkOrInterruptTransfer.TransferFlags &
         USBD_TRANSFER_DIRECTION_IN) == 0) {
        return FALSE;
    }

    return TRUE;
}

VOID
PalmRawTapInUrbCompletion(
    _In_ WDFREQUEST Request,
    _In_ WDFIOTARGET Target,
    _In_ PWDF_REQUEST_COMPLETION_PARAMS CompletionParams,
    _In_ WDFCONTEXT Context
    )
{
    PURB urb;
    LONG64 sequence;
    ULONGLONG atUs;
    ULONG transferLength;
    UCHAR reportId;
    USHORT scanTime;
    UCHAR contactCount;
    PUCHAR bytes;
    PALM_A460_BRIDGE_FRAME a460Frame;
    LONG64 a460BridgeSequence;
    CHAR rawHexA[65];
    CHAR rawHexB[65];
    ULONG rawIndex;
    static const CHAR rawHexDigits[] = "0123456789ABCDEF";

    UNREFERENCED_PARAMETER(Target);

    urb = (PURB)Context;
    sequence = InterlockedIncrement64(&g_PalmRawTapInCompleteSequence);
    atUs = KeQueryInterruptTime() / 10ULL;

    transferLength = 0;
    reportId = 0xFF;
    scanTime = 0;
    contactCount = 0;
    bytes = NULL;

    if (urb != NULL &&
        urb->UrbHeader.Function == URB_FUNCTION_BULK_OR_INTERRUPT_TRANSFER) {

        transferLength =
            urb->UrbBulkOrInterruptTransfer.TransferBufferLength;

        if (NT_SUCCESS(CompletionParams->IoStatus.Status) &&
            USBD_SUCCESS(urb->UrbHeader.Status) &&
            transferLength > 0) {

            bytes = PalmRawTapGetTransferBuffer(urb);

            if (bytes != NULL) {
                reportId = bytes[0];

                if (reportId == 0x81 &&
                    transferLength >= 64) {

                    scanTime = (USHORT)(
                        ((USHORT)bytes[61]) |
                        ((USHORT)bytes[62] << 8)
                        );

                    contactCount = bytes[63];

                    
if (g_A460RawTapCallbackObject != NULL &&
                    
    InterlockedCompareExchange(&g_A460RawTapNotifyEnabled, 0, 0) != 0) {
                    
    RtlZeroMemory(&a460Frame, sizeof(a460Frame));
                    
    a460BridgeSequence = InterlockedIncrement64(&g_A460RawTapBridgeSequence);
                    
    a460Frame.Magic = PALM_A460_BRIDGE_MAGIC;
                    
    a460Frame.Size = sizeof(a460Frame);
                    
    a460Frame.Version = PALM_A460_BRIDGE_VERSION;
                    
    a460Frame.BridgeSequence = a460BridgeSequence;
                    
    a460Frame.UsbSequence = sequence;
                    
    a460Frame.AtUs = atUs;
                    
    a460Frame.ScanTime = scanTime;
                    
    a460Frame.ContactCount = contactCount;
                    
    a460Frame.ReportId = reportId;
                    
    RtlCopyMemory(a460Frame.Raw, bytes, 64);
                    
    ExNotifyCallback(g_A460RawTapCallbackObject, &a460Frame, NULL);
                    
    DbgPrintEx(
                    
        DPFLTR_IHVDRIVER_ID,
                    
        DPFLTR_ERROR_LEVEL,
                    
        "PalmRawUsbTap V1.3: BRIDGE-TX bseq=%I64d usbSeq=%I64d atUs=%I64u scan=%u count=%u usbB1=0x%02X usbB2=0x%02X action=NOTIFY-ONLY\\n",
                    
        a460BridgeSequence,
                    
        sequence,
                    
        atUs,
                    
        (ULONG)scanTime,
                    
        (ULONG)contactCount,
                    
        (ULONG)bytes[1],
                    
        (ULONG)bytes[2]);
                    
}

                    
for (rawIndex = 0; rawIndex < 32; rawIndex++) {
                    
    rawHexA[rawIndex * 2] =
                    
        rawHexDigits[(bytes[rawIndex] >> 4) & 0x0F];
                    
    rawHexA[(rawIndex * 2) + 1] =
                    
        rawHexDigits[bytes[rawIndex] & 0x0F];
                    
    rawHexB[rawIndex * 2] =
                    
        rawHexDigits[(bytes[rawIndex + 32] >> 4) & 0x0F];
                    
    rawHexB[(rawIndex * 2) + 1] =
                    
        rawHexDigits[bytes[rawIndex + 32] & 0x0F];
                    
}
                    
rawHexA[64] = '\0';
                    
rawHexB[64] = '\0';

                    
DbgPrintEx(
                    
    DPFLTR_IHVDRIVER_ID,
                    
    DPFLTR_ERROR_LEVEL,
                    
    "PalmRawUsbTap V1.2: RAW64-A seq=%I64d atUs=%I64u scan=%u count=%u bytes=%s\n",
                    
    sequence,
                    
    atUs,
                    
    (ULONG)scanTime,
                    
    (ULONG)contactCount,
                    
    rawHexA
                    
    );

                    
DbgPrintEx(
                    
    DPFLTR_IHVDRIVER_ID,
                    
    DPFLTR_ERROR_LEVEL,
                    
    "PalmRawUsbTap V1.2: RAW64-B seq=%I64d atUs=%I64u scan=%u count=%u bytes=%s\n",
                    
    sequence,
                    
    atUs,
                    
    (ULONG)scanTime,
                    
    (ULONG)contactCount,
                    
    rawHexB
                    
    );
                }
            }
        }

        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "PalmRawUsbTap V1: IN-COMP seq=%I64d atUs=%I64u nt=0x%08X usbd=0x%08X len=%lu report=0x%02X pipe=%p scan=%u count=%u\n",
            sequence,
            atUs,
            (ULONG)CompletionParams->IoStatus.Status,
            (ULONG)urb->UrbHeader.Status,
            transferLength,
            reportId,
            urb->UrbBulkOrInterruptTransfer.PipeHandle,
            (ULONG)scanTime,
            (ULONG)contactCount
            );
    }
    else {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "PalmRawUsbTap V1: IN-COMP-INVALID seq=%I64d atUs=%I64u nt=0x%08X\n",
            sequence,
            atUs,
            (ULONG)CompletionParams->IoStatus.Status
            );
    }

    WdfRequestCompleteWithInformation(
        Request,
        CompletionParams->IoStatus.Status,
        CompletionParams->IoStatus.Information
        );
}

VOID
PalmRawTapEvtIoInternalDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
{
    WDFDEVICE device;
    WDFIOTARGET target;
    PIRP irp;
    PIO_STACK_LOCATION stack;
    PURB urb;
    BOOLEAN tracked;
    BOOLEAN sent;
    NTSTATUS status;
    WDF_REQUEST_SEND_OPTIONS sendOptions;

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    device = WdfIoQueueGetDevice(Queue);
    target = WdfDeviceGetIoTarget(device);

    urb = NULL;
    tracked = FALSE;

    if (IoControlCode == IOCTL_INTERNAL_USB_SUBMIT_URB) {
        irp = WdfRequestWdmGetIrp(Request);

        if (irp != NULL) {
            stack = IoGetCurrentIrpStackLocation(irp);

            if (stack != NULL) {
                urb = (PURB)stack->Parameters.Others.Argument1;
                tracked = PalmRawTapIsInboundBulkOrInterruptUrb(urb);
            }
        }
    }

    WdfRequestFormatRequestUsingCurrentType(Request);

    if (tracked) {
        WdfRequestSetCompletionRoutine(
            Request,
            PalmRawTapInUrbCompletion,
            (WDFCONTEXT)urb
            );

        sent = WdfRequestSend(
            Request,
            target,
            WDF_NO_SEND_OPTIONS
            );
    }
    else {
        WDF_REQUEST_SEND_OPTIONS_INIT(
            &sendOptions,
            WDF_REQUEST_SEND_OPTION_SEND_AND_FORGET
            );

        sent = WdfRequestSend(
            Request,
            target,
            &sendOptions
            );
    }

    if (!sent) {
        status = WdfRequestGetStatus(Request);

        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "PalmRawUsbTap V1: FORWARD-FAIL ioctl=0x%08X status=0x%08X tracked=%u\n",
            IoControlCode,
            (ULONG)status,
            tracked ? 1U : 0U
            );

        WdfRequestComplete(Request, status);
    }
}

NTSTATUS
PalmRawTapEvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
    )
{
    NTSTATUS status;
    WDFDEVICE device;
    WDFQUEUE internalQueue;
    WDF_IO_QUEUE_CONFIG queueConfig;

    UNREFERENCED_PARAMETER(Driver);

    WdfFdoInitSetFilter(DeviceInit);

    status = WdfDeviceCreate(
        &DeviceInit,
        WDF_NO_OBJECT_ATTRIBUTES,
        &device
        );

    if (!NT_SUCCESS(status)) {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "PalmRawUsbTap V1: DEVICE-CREATE-FAIL status=0x%08X\n",
            (ULONG)status
            );
        return status;
    }

    WDF_IO_QUEUE_CONFIG_INIT(
        &queueConfig,
        WdfIoQueueDispatchParallel
        );

    queueConfig.EvtIoInternalDeviceControl =
        PalmRawTapEvtIoInternalDeviceControl;

    status = WdfIoQueueCreate(
        device,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        &internalQueue
        );

    if (!NT_SUCCESS(status)) {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "PalmRawUsbTap V1: QUEUE-CREATE-FAIL status=0x%08X\n",
            (ULONG)status
            );
        return status;
    }

    status = WdfDeviceConfigureRequestDispatching(
        device,
        internalQueue,
        WdfRequestTypeDeviceControlInternal
        );

    if (!NT_SUCCESS(status)) {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "PalmRawUsbTap V1: DISPATCH-CONFIG-FAIL status=0x%08X\n",
            (ULONG)status
            );
        return status;
    }

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_ERROR_LEVEL,
        "PalmRawUsbTap V1: DEVICE-ADD mode=LOWER-FILTER readOnly=1 target=USB_VID_056A_PID_0355 trace=IN_URB_REPORT_ID_ONLY\n"
        );

    return STATUS_SUCCESS;
}

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    WDF_DRIVER_CONFIG config;
    WDF_OBJECT_ATTRIBUTES driverAttributes;
    NTSTATUS status;
    NTSTATUS bridgeStatus;

    WDF_DRIVER_CONFIG_INIT(
        &config,
        PalmRawTapEvtDeviceAdd
        );

    WDF_OBJECT_ATTRIBUTES_INIT(&driverAttributes);
    driverAttributes.EvtCleanupCallback = PalmA460RawTapDriverCleanup;
    bridgeStatus = PalmA460RawTapCreateOrOpenCallback();

    status = WdfDriverCreate(
        DriverObject,
        RegistryPath,
        &driverAttributes,
        &config,
        WDF_NO_HANDLE
        );

    if (!NT_SUCCESS(status)) {         PalmA460RawTapShutdownBridge();     }      if (NT_SUCCESS(status)) {         DbgPrintEx(             DPFLTR_IHVDRIVER_ID,             DPFLTR_ERROR_LEVEL,             "PalmRawUsbTap V1.3: BRIDGE-START status=0x%08X callback=%p object=TEMPORARY-NAMED-SHARED notifyOnly=1 requestBehavior=UNCHANGED\\n",             (ULONG)bridgeStatus,             g_A460RawTapCallbackObject);
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "PalmRawUsbTap V1: DRIVER-START version=0.1.3.0 behavior=PASS-THROUGH telemetry=USB-IN-COMP reportBytes=READ-ONLY\n"
            );
    }

    return status;
}

