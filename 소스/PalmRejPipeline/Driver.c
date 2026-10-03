#include "Driver.h"

static volatile LONG g_Pv1bNextDeviceId = 0;
static volatile LONG64 g_Pv1bNextCycleId = 0;

static VOID
Pv1bLogEvent(
    _In_z_ PCSTR Event,
    _In_opt_ PDEVICE_CONTEXT Ctx,
    _In_ LONGLONG A,
    _In_ LONGLONG B,
    _In_ LONGLONG C
    )
{
    if (Ctx == NULL) {
        DbgPrint(
            "PalmRejPipeline PV1I: %s deviceId=0 gen=0 prepared=0 d0=0 smio=0 surprise=0 deferredCount=0 boundaryRelease=0 episodeActive=0 episodeId=0 fatal=0 a=%I64d b=%I64d c=%I64d\n",
            Event,
            A,
            B,
            C
            );
        return;
    }

    DbgPrint(
        "PalmRejPipeline PV1I: %s deviceId=%ld gen=%ld prepared=%ld d0=%ld smio=%ld surprise=%ld deferredCount=%ld boundaryRelease=%ld episodeActive=%ld episodeId=%I64d fatal=%ld a=%I64d b=%I64d c=%I64d\n",
        Event,
        Ctx->DeviceId,
        InterlockedCompareExchange(&Ctx->Generation, 0, 0),
        InterlockedCompareExchange(&Ctx->HardwarePrepared, 0, 0),
        InterlockedCompareExchange(&Ctx->DeviceInD0, 0, 0),
        InterlockedCompareExchange(&Ctx->SelfManagedActive, 0, 0),
        InterlockedCompareExchange(&Ctx->SurpriseRemoved, 0, 0),
        InterlockedCompareExchange(&Ctx->DeferredValid, 0, 0),
        InterlockedCompareExchange(&Ctx->BoundaryReleasePending, 0, 0),
        InterlockedCompareExchange(&Ctx->PhysicalEpisodeActive, 0, 0),
        InterlockedCompareExchange64(&Ctx->EpisodeId, 0, 0),
        InterlockedCompareExchange(&Ctx->FatalBypass, 0, 0),
        A,
        B,
        C
        );
}

/*
 * PV0112 (PalmRej 0.1.12): FatalBypass no longer latches for the session.
 *
 * 0.1.11 real-use log, 366.20 s: a lookahead read came back with 640 bytes
 * (HIDClass drained a ten-report backlog into one read).  PV1K takes exactly
 * 64 there, so it set FatalBypass, and only EvtDeviceD0Entry cleared it: the
 * pipeline passed everything through for the last 18 minutes, and nothing
 * said so, because every PV1K line is plain DbgPrint.
 *
 * Every entry now goes through here and names its cause with DbgPrintEx at
 * IHVDRIVER/ERROR, the level DebugView captures.  A cause that means the
 * request-ownership model itself broke is sticky and keeps the PV1K latch;
 * any other cause is left at the next all-up report the reader receives
 * through the bypass (Pv0112TryLeaveBypass).  The bit is OR-ed in, so a
 * later recoverable entry never clears a sticky one.
 *
 * Called with and without StateLock held: takes no lock.
 */
static volatile LONG64 g_V0112BypassEnters = 0;
static volatile LONG64 g_V0112BypassExits = 0;
static volatile LONG64 g_V0112BatchCloses = 0;

static VOID
Pv0112EnterBypass(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_z_ PCSTR Cause,
    _In_ LONG64 CycleId,
    _In_ BOOLEAN Sticky
    )
{
    LONG previous;

    previous = InterlockedOr(
        &Ctx->FatalBypass,
        Sticky ?
            (PV0112_BYPASS_ON | PV0112_BYPASS_STICKY) :
            PV0112_BYPASS_ON
        );

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_ERROR_LEVEL,
        "PalmRejPipeline PV0112: BYPASS-ENTER cause=%s cycle=%I64d dev=%ld sticky=%ld was=0x%lX episodeActive=%ld deferredCount=%ld boundaryRelease=%ld fatalEvents=%I64d enters=%I64d\n",
        Cause,
        CycleId,
        Ctx->DeviceId,
        Sticky ? 1L : 0L,
        (ULONG)previous,
        InterlockedCompareExchange(&Ctx->PhysicalEpisodeActive, 0, 0),
        InterlockedCompareExchange(&Ctx->DeferredValid, 0, 0),
        InterlockedCompareExchange(&Ctx->BoundaryReleasePending, 0, 0),
        InterlockedCompareExchange64(&Ctx->Counters.FatalBypassEvents, 0, 0),
        InterlockedIncrement64(&g_V0112BypassEnters)
        );
}

static BOOLEAN
Pv1bIsInputReportRequest(
    _In_ PWDF_REQUEST_PARAMETERS Params
    )
{
    if (Params->Type == WdfRequestTypeRead) {
        return TRUE;
    }

    if ((Params->Type == WdfRequestTypeDeviceControl ||
         Params->Type == WdfRequestTypeDeviceControlInternal) &&
        Params->Parameters.DeviceIoControl.IoControlCode ==
            IOCTL_HID_READ_REPORT) {
        return TRUE;
    }

    return FALSE;
}

static BOOLEAN
Pv1bIsNullFileObject(
    _In_opt_ PFILE_OBJECT FileObject
    )
{
    return FileObject == NULL ? TRUE : FALSE;
}

static PFILE_OBJECT
Pv1bGetCurrentFileObject(
    _In_ WDFREQUEST Request
    )
{
    PIRP irp;
    PIO_STACK_LOCATION stack;

    irp = WdfRequestWdmGetIrp(Request);
    if (irp == NULL) {
        return NULL;
    }

    if (irp->CurrentLocation > (irp->StackCount + 1)) {
        return NULL;
    }

    stack = IoGetCurrentIrpStackLocation(irp);
    if (stack == NULL) {
        return NULL;
    }

    return stack->FileObject;
}

static PFILE_OBJECT
Pv1bGetNextFileObject(
    _In_ WDFREQUEST Request
    )
{
    PIRP irp;
    PIO_STACK_LOCATION stack;

    irp = WdfRequestWdmGetIrp(Request);
    if (irp == NULL || irp->CurrentLocation <= 1) {
        return NULL;
    }

    stack = IoGetNextIrpStackLocation(irp);
    if (stack == NULL) {
        return NULL;
    }

    return stack->FileObject;
}

static PFILE_OBJECT
Pv1bGetOriginalFileObject(
    _In_ WDFREQUEST Request
    )
{
    PIRP irp;

    irp = WdfRequestWdmGetIrp(Request);
    if (irp == NULL) {
        return NULL;
    }

    return irp->Tail.Overlay.OriginalFileObject;
}

static VOID
Pv1bLogRequestContext(
    _In_z_ PCSTR Event,
    _In_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _In_ LONG64 CycleId,
    _In_ LONG Phase,
    _In_ NTSTATUS Status
    )
{
    PIRP irp;
    PFILE_OBJECT originalFileObject;
    PFILE_OBJECT currentFileObject;
    PFILE_OBJECT nextFileObject;
    KPROCESSOR_MODE requestorMode;
    BOOLEAN canceled;
    BOOLEAN irpCancel;

    irp = WdfRequestWdmGetIrp(Request);
    originalFileObject = Pv1bGetOriginalFileObject(Request);
    currentFileObject = Pv1bGetCurrentFileObject(Request);
    nextFileObject = Pv1bGetNextFileObject(Request);
    requestorMode = WdfRequestGetRequestorMode(Request);
    canceled = WdfRequestIsCanceled(Request);
    irpCancel = (irp != NULL && irp->Cancel) ? TRUE : FALSE;

    DbgPrint(
        "PalmRejPipeline PV1I: %s cycle=%I64d dev=%ld phase=%ld status=0x%08X canceled=%ld irpCancel=%ld reqMode=%ld irp=%p originalFO=%p currentFO=%p nextFO=%p\n",
        Event,
        CycleId,
        Ctx->DeviceId,
        Phase,
        (ULONG)Status,
        canceled ? 1L : 0L,
        irpCancel ? 1L : 0L,
        (LONG)requestorMode,
        irp,
        originalFileObject,
        currentFileObject,
        nextFileObject
        );
}

static NTSTATUS
Pv1bRetrieveOutput(
    _In_ WDFREQUEST Request,
    _Outptr_result_bytebuffer_(*Length) PVOID* Buffer,
    _Out_ size_t* Length
    )
{
    return WdfRequestRetrieveOutputBuffer(
        Request,
        1u,
        Buffer,
        Length
        );
}

_Success_(return)
static BOOLEAN
Pv1bCopyReportFromRequest(
    _In_ WDFREQUEST Request,
    _In_ ULONG_PTR Information,
    _Out_writes_bytes_(PV1B_REPORT_BYTES) UCHAR* Destination
    )
{
    NTSTATUS status;
    PVOID outputBuffer;
    size_t outputLength;

    outputBuffer = NULL;
    outputLength = 0u;

    if (Information != PV1B_REPORT_BYTES) {
        return FALSE;
    }

    status = Pv1bRetrieveOutput(
        Request,
        &outputBuffer,
        &outputLength
        );

    if (!NT_SUCCESS(status) ||
        outputBuffer == NULL ||
        outputLength < PV1B_REPORT_BYTES) {
        return FALSE;
    }

    RtlCopyMemory(
        Destination,
        outputBuffer,
        PV1B_REPORT_BYTES
        );

    return TRUE;
}

static BOOLEAN
Pv1bCopyReportToRequest(
    _In_ WDFREQUEST Request,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Source
    )
{
    NTSTATUS status;
    PVOID outputBuffer;
    size_t outputLength;

    outputBuffer = NULL;
    outputLength = 0u;

    status = Pv1bRetrieveOutput(
        Request,
        &outputBuffer,
        &outputLength
        );

    if (!NT_SUCCESS(status) ||
        outputBuffer == NULL ||
        outputLength < PV1B_REPORT_BYTES) {
        return FALSE;
    }

    RtlCopyMemory(
        outputBuffer,
        Source,
        PV1B_REPORT_BYTES
        );

    return TRUE;
}


static VOID
Pv1jRememberUpperVisibleTouch(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _In_ NTSTATUS Status,
    _In_ ULONG_PTR Information
    )
{
    NTSTATUS retrieveStatus;
    PVOID outputBuffer;
    size_t outputLength;
    BOOLEAN validTouch;
    UCHAR localReport[PV1B_REPORT_BYTES];

    retrieveStatus = STATUS_UNSUCCESSFUL;
    outputBuffer = NULL;
    outputLength = 0u;
    validTouch = FALSE;
    RtlZeroMemory(localReport, sizeof(localReport));

    if (NT_SUCCESS(Status) &&
        Information >= PV1B_REPORT_BYTES) {
        retrieveStatus = Pv1bRetrieveOutput(
            Request,
            &outputBuffer,
            &outputLength
            );

        if (NT_SUCCESS(retrieveStatus) &&
            outputBuffer != NULL &&
            outputLength >= PV1B_REPORT_BYTES &&
            ((const UCHAR*)outputBuffer)[0] == PV1B_REPORT_ID_TOUCH) {
            RtlCopyMemory(
                localReport,
                outputBuffer,
                PV1B_REPORT_BYTES
                );
            validTouch = TRUE;
        }
    }

    WdfSpinLockAcquire(Ctx->StateLock);

    if (validTouch) {
        RtlCopyMemory(
            Ctx->LastUpperTouchReport,
            localReport,
            PV1B_REPORT_BYTES
            );
        Ctx->LastUpperTouchInformation = Information;
        InterlockedExchange(
            &Ctx->LastUpperTouchValid,
            1
            );
    }
    else {
        RtlZeroMemory(
            Ctx->LastUpperTouchReport,
            PV1B_REPORT_BYTES
            );
        Ctx->LastUpperTouchInformation = 0u;
        InterlockedExchange(
            &Ctx->LastUpperTouchValid,
            0
            );
    }

    WdfSpinLockRelease(Ctx->StateLock);
}


static BOOLEAN
Pv1fCanIdleBatchDirect(
    _Inout_ PDEVICE_CONTEXT Ctx
    )
{
    BOOLEAN safe;

    safe = FALSE;

    WdfSpinLockAcquire(
        Ctx->StateLock
        );

    if (Ctx->FatalBypass == 0 &&
        Ctx->DeferredValid == 0 &&
        Ctx->BoundaryReleasePending == 0 &&
        Ctx->PhysicalEpisodeActive == 0 &&
        Ctx->ActiveLookaheadRequest == WDF_NO_HANDLE) {
        safe = TRUE;
    }

    WdfSpinLockRelease(
        Ctx->StateLock
        );

    return safe;
}

static BOOLEAN
Pv1fIsSupportedIdleBatchCompletion(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _In_ ULONG_PTR Information
    )
{
    NTSTATUS status;
    PVOID outputBuffer;
    size_t outputLength;
    ULONG_PTR reportCount;

    if (Information <= PV1B_REPORT_BYTES ||
        (Information % PV1B_REPORT_BYTES) != 0u) {
        return FALSE;
    }

    reportCount = Information / PV1B_REPORT_BYTES;
    if (reportCount < 2u ||
        reportCount > PV1F_MAX_IDLE_BATCH_REPORTS) {
        return FALSE;
    }

    outputBuffer = NULL;
    outputLength = 0u;
    status = Pv1bRetrieveOutput(
        Request,
        &outputBuffer,
        &outputLength
        );

    if (!NT_SUCCESS(status) ||
        outputBuffer == NULL ||
        outputLength < Information) {
        return FALSE;
    }

    return Pv1fCanIdleBatchDirect(Ctx);
}

static VOID
Pv1fLogCompletionShape(
    _In_z_ PCSTR Event,
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _In_ LONG64 CycleId,
    _In_ ULONG_PTR Information
    )
{
    NTSTATUS status;
    PVOID outputBuffer;
    size_t outputLength;
    ULONG_PTR validLength;
    ULONG_PTR reportCount;
    ULONG_PTR remainder;
    PUCHAR b;

    outputBuffer = NULL;
    outputLength = 0u;
    validLength = 0u;
    reportCount = 0u;
    remainder = 0u;

    status = Pv1bRetrieveOutput(
        Request,
        &outputBuffer,
        &outputLength
        );

    if (!NT_SUCCESS(status) || outputBuffer == NULL) {
        DbgPrint(
            "PalmRejPipeline PV1I: %s cycle=%I64d dev=%ld info=%Iu retrieveStatus=0x%08X outputLength=%Iu action=LOG-ONLY\n",
            Event,
            CycleId,
            Ctx->DeviceId,
            Information,
            (ULONG)status,
            outputLength
            );
        return;
    }

    validLength = Information;
    if (validLength > outputLength) {
        validLength = outputLength;
    }

    reportCount = Information / PV1B_REPORT_BYTES;
    remainder = Information % PV1B_REPORT_BYTES;
    b = (PUCHAR)outputBuffer;

    DbgPrint(
        "PalmRejPipeline PV1I: %s cycle=%I64d dev=%ld info=%Iu outputLength=%Iu validLength=%Iu report64Count=%Iu remainder=%Iu action=LOG-ONLY\n",
        Event,
        CycleId,
        Ctx->DeviceId,
        Information,
        outputLength,
        validLength,
        reportCount,
        remainder
        );

    if (Information >= 16u &&
        outputLength >= 16u) {
        DbgPrint(
            "PalmRejPipeline PV1I: BATCH64-R0 cycle=%I64d dev=%ld bytes=%02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X action=LOG-ONLY\n",
            CycleId,
            Ctx->DeviceId,
            (ULONG)b[0], (ULONG)b[1], (ULONG)b[2], (ULONG)b[3],
            (ULONG)b[4], (ULONG)b[5], (ULONG)b[6], (ULONG)b[7],
            (ULONG)b[8], (ULONG)b[9], (ULONG)b[10], (ULONG)b[11],
            (ULONG)b[12], (ULONG)b[13], (ULONG)b[14], (ULONG)b[15]
            );
    }

    if (Information >= (PV1B_REPORT_BYTES * 2u) &&
        outputLength >= (PV1B_REPORT_BYTES + 16u)) {
        PUCHAR b1;

        b1 = b + PV1B_REPORT_BYTES;

        DbgPrint(
            "PalmRejPipeline PV1I: BATCH64-R1 cycle=%I64d dev=%ld bytes=%02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X action=LOG-ONLY\n",
            CycleId,
            Ctx->DeviceId,
            (ULONG)b1[0], (ULONG)b1[1], (ULONG)b1[2], (ULONG)b1[3],
            (ULONG)b1[4], (ULONG)b1[5], (ULONG)b1[6], (ULONG)b1[7],
            (ULONG)b1[8], (ULONG)b1[9], (ULONG)b1[10], (ULONG)b1[11],
            (ULONG)b1[12], (ULONG)b1[13], (ULONG)b1[14], (ULONG)b1[15]
            );
    }
}

static VOID
Pv1bComputeTipAudit(
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report,
    _Out_ PULONG ValidSlotCount,
    _Out_ PULONG ActiveTipCount,
    _Out_ PULONG TipMask,
    _Out_ PULONG ConfidenceMask
    )
{
    ULONG slot;
    ULONG validCount = 0u;
    ULONG activeTips = 0u;
    ULONG tipMask = 0u;
    ULONG confidenceMask = 0u;

    for (slot = 0u; slot < PV1B_CONTACT_SLOT_COUNT; ++slot) {
        ULONG stateOffset = PV1B_CONTACT_SLOT_BASE_OFFSET + (slot * PV1B_CONTACT_SLOT_BYTES);
        UCHAR state = Report[stateOffset];

        // 0xFF is the observed Wacom unused-slot sentinel.
        if (state == PV1B_STATE_UNUSED_SENTINEL) {
            continue;
        }

        validCount++;
        if ((state & PV1B_STATE_TIP_BIT) != 0u) {
            tipMask |= (1u << slot);
            activeTips++;
        }
        if ((state & PV1B_STATE_CONFIDENCE_BIT) != 0u) {
            confidenceMask |= (1u << slot);
        }
    }

    *ValidSlotCount = validCount;
    *ActiveTipCount = activeTips;
    *TipMask = tipMask;
    *ConfidenceMask = confidenceMask;
}

static BOOLEAN
Pv1hIsPartialConfidenceBoundary(
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report
    )
{
    ULONG validSlotCount;
    ULONG activeTipCount;
    ULONG tipMask;
    ULONG confidenceMask;
    ULONG reportedCount;
    ULONG confidenceCount;
    ULONG slot;

    if (Report[0] != PV1B_REPORT_ID_TOUCH) {
        return FALSE;
    }

    Pv1bComputeTipAudit(
        Report,
        &validSlotCount,
        &activeTipCount,
        &tipMask,
        &confidenceMask
        );

    UNREFERENCED_PARAMETER(tipMask);

    reportedCount = (ULONG)Report[PV1B_CONTACT_COUNT_OFFSET];

    // A55R1: among 1,123 warm-up boundary restores this shape was
    // absent from 371 classified normal restores and 622 unknown restores,
    // while appearing in hand-edge restores including A54 visible leak #1.
    // Keep this narrower than the offline A55 rule by also requiring the
    // number of valid slots to exactly match ContactCount.
    if (reportedCount < 2u ||
        activeTipCount != 0u ||
        validSlotCount != reportedCount ||
        confidenceMask == 0u) {
        return FALSE;
    }

    confidenceCount = 0u;
    for (slot = 0u; slot < PV1B_CONTACT_SLOT_COUNT; ++slot) {
        if ((confidenceMask & (1u << slot)) != 0u) {
            confidenceCount++;
        }
    }

    return (confidenceCount < validSlotCount) ? TRUE : FALSE;
}

static ULONG
Pv1eEvaluateFutureVetoReason(
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report
    )
{
    ULONG validSlotCount;
    ULONG activeTipCount;
    ULONG tipMask;
    ULONG confidenceMask;
    ULONG reportedCount;
    ULONG reasonMask;

    if (Report[0] != PV1B_REPORT_ID_TOUCH) {
        return 0u;
    }

    Pv1bComputeTipAudit(
        Report,
        &validSlotCount,
        &activeTipCount,
        &tipMask,
        &confidenceMask
        );

    UNREFERENCED_PARAMETER(validSlotCount);

    reportedCount = (ULONG)Report[PV1B_CONTACT_COUNT_OFFSET];
    reasonMask = 0u;

    // A normal active Tip observed without Confidence was absent from all
    // 1,730 active normal 1F/2F reports in A24, but appeared in hand-edge
    // episodes before upper delivery.
    if (activeTipCount != 0u &&
        (tipMask & ~confidenceMask) != 0u) {
        reasonMask |= PV1E_FUTURE_VETO_REASON_CONFIDENCE_LOSS;
    }

    // Project policy explicitly allows generic 3+ touch to be suppressed.
    if (reportedCount >= 3u) {
        reasonMask |= PV1E_FUTURE_VETO_REASON_THREEPLUS;
    }

    // PV1H: an initial warm-up episode that ends on a non-active release
    // with 2+ valid reported slots but Confidence surviving on only a proper
    // subset is treated as boundary-local palm evidence. The ordinary PV1F
    // boundary commit then suppresses only report 1; FIFO/release semantics
    // are otherwise unchanged.
    if (Pv1hIsPartialConfidenceBoundary(Report)) {
        reasonMask |= PV1H_FUTURE_VETO_REASON_PARTIAL_CONFIDENCE_BOUNDARY;
    }

    return reasonMask;
}

static VOID
Pv1eObserveFutureVetoCandidate(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report,
    _In_ ULONG PhysicalIndex
    )
{
    ULONG reasonMask;
    ULONG previousReasonMask;

    reasonMask = Pv1eEvaluateFutureVetoReason(Report);

    if (reasonMask == 0u) {
        return;
    }

    previousReasonMask = RequestCtx->FutureVetoReasonMask;
    RequestCtx->FutureVetoReasonMask |= reasonMask;

    if (InterlockedCompareExchange(
            &RequestCtx->FutureVetoCandidate,
            1,
            0
            ) == 0) {
        RequestCtx->FutureVetoFirstPhysicalIndex = PhysicalIndex;
        InterlockedIncrement64(&Ctx->Counters.FutureVetoCandidates);
    }

    DbgPrint(
        "PalmRejPipeline PV1I: FUTURE-VETO-CANDIDATE cycle=%I64d dev=%ld physicalIndex=%lu reasonNow=0x%02lX reasonAccum=0x%02lX previousReason=0x%02lX\n",
        RequestCtx->CycleId,
        Ctx->DeviceId,
        PhysicalIndex,
        reasonMask,
        RequestCtx->FutureVetoReasonMask,
        previousReasonMask
        );
}

static VOID
Pv1eBuildZeroTouchReport(
    _Out_writes_bytes_(PV1B_REPORT_BYTES) UCHAR* Report
    )
{
    RtlZeroMemory(Report, PV1B_REPORT_BYTES);
    Report[0] = PV1B_REPORT_ID_TOUCH;
}

static VOID
Pv1bLogRawPhysicalReport(
    _In_ LONG64 CycleId,
    _In_ ULONG PhysicalIndex,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report
    )
{
    static const CHAR HexDigits[] = "0123456789ABCDEF";
    CHAR rawHex[(PV1B_REPORT_BYTES * 2u) + 1u];
    ULONG i;

    // CycleId == 0 is a deferred FIFO re-delivery, not a new physical frame.
    if (CycleId <= 0) {
        return;
    }

    for (i = 0u; i < PV1B_REPORT_BYTES; ++i) {
        UCHAR value = Report[i];
        rawHex[(i * 2u)] = HexDigits[(value >> 4) & 0x0Fu];
        rawHex[(i * 2u) + 1u] = HexDigits[value & 0x0Fu];
    }

    rawHex[PV1B_REPORT_BYTES * 2u] = '\0';

    DbgPrint(
        "PalmRejPipeline PV1I: RAW64-PHYSICAL cycle=%I64d physicalIndex=%lu raw=%s\n",
        CycleId,
        PhysicalIndex,
        rawHex
        );
}

static VOID
Pv1bLogTipAudit(
    _In_ PDEVICE_CONTEXT Ctx,
    _In_ LONG64 CycleId,
    _In_ ULONG PhysicalIndex,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report
    )
{
    ULONG validSlotCount;
    ULONG activeTipCount;
    ULONG tipMask;
    ULONG confidenceMask;
    ULONG reportedCount;
    LONG legacyActive;
    LONG tipActive;

    Pv1bComputeTipAudit(Report, &validSlotCount, &activeTipCount, &tipMask, &confidenceMask);
    reportedCount = (ULONG)Report[PV1B_CONTACT_COUNT_OFFSET];
    legacyActive = (Report[0] == PV1B_REPORT_ID_TOUCH && reportedCount != 0u) ? 1L : 0L;
    tipActive = (Report[0] == PV1B_REPORT_ID_TOUCH && activeTipCount != 0u) ? 1L : 0L;

    DbgPrint(
        "PalmRejPipeline PV1I: TIP-STATE-AUDIT cycle=%I64d dev=%ld physicalIndex=%lu reportedCount=%lu validSlotCount=%lu activeTipCount=%lu tipMask=0x%03lX confidenceMask=0x%03lX legacyActive=%ld tipActive=%ld states=%02X,%02X,%02X,%02X,%02X,%02X,%02X,%02X,%02X,%02X\n",
        CycleId, Ctx->DeviceId, PhysicalIndex, reportedCount, validSlotCount,
        activeTipCount, tipMask, confidenceMask, legacyActive, tipActive,
        (ULONG)Report[1], (ULONG)Report[7], (ULONG)Report[13], (ULONG)Report[19],
        (ULONG)Report[25], (ULONG)Report[31], (ULONG)Report[37], (ULONG)Report[43],
        (ULONG)Report[49], (ULONG)Report[55]
        );

    if (legacyActive != tipActive) {
        DbgPrint(
            "PalmRejPipeline PV1I: TIP-LEGACY-MISMATCH cycle=%I64d dev=%ld physicalIndex=%lu reportedCount=%lu activeTipCount=%lu tipMask=0x%03lX legacyActive=%ld tipActive=%ld\n",
            CycleId, Ctx->DeviceId, PhysicalIndex, reportedCount, activeTipCount,
            tipMask, legacyActive, tipActive
            );
    }
}

static VOID
Pv1bLogReportMeta(
    _In_z_ PCSTR Event,
    _In_ PDEVICE_CONTEXT Ctx,
    _In_ LONG64 CycleId,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report,
    _In_ ULONG_PTR Information
    )
{
    USHORT scanTime = (USHORT)(((USHORT)Report[PV1B_SCAN_TIME_LOW_OFFSET]) |
        (((USHORT)Report[PV1B_SCAN_TIME_HIGH_OFFSET]) << 8));

    DbgPrint(
        "PalmRejPipeline PV1I: %s cycle=%I64d dev=%ld info=%Iu reportId=0x%02X contactCount=%u scanTime=%u\n",
        Event, CycleId, Ctx->DeviceId, Information, (ULONG)Report[0],
        (ULONG)Report[PV1B_CONTACT_COUNT_OFFSET], (ULONG)scanTime
        );

    Pv1bLogTipAudit(Ctx, CycleId, 1u, Report);
    Pv1bLogRawPhysicalReport(CycleId, 1u, Report);
}

static VOID
Pv1bLogLookaheadReportMeta(
    _In_ PDEVICE_CONTEXT Ctx,
    _In_ LONG64 CycleId,
    _In_ ULONG PhysicalIndex,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report,
    _In_ ULONG_PTR Information
    )
{
    USHORT scanTime = (USHORT)(((USHORT)Report[PV1B_SCAN_TIME_LOW_OFFSET]) |
        (((USHORT)Report[PV1B_SCAN_TIME_HIGH_OFFSET]) << 8));

    DbgPrint(
        "PalmRejPipeline PV1I: LOOKAHEAD-REPORT-META cycle=%I64d dev=%ld physicalIndex=%lu info=%Iu reportId=0x%02X contactCount=%u scanTime=%u\n",
        CycleId, Ctx->DeviceId, PhysicalIndex, Information, (ULONG)Report[0],
        (ULONG)Report[PV1B_CONTACT_COUNT_OFFSET], (ULONG)scanTime
        );

    Pv1bLogTipAudit(Ctx, CycleId, PhysicalIndex, Report);
    Pv1bLogRawPhysicalReport(CycleId, PhysicalIndex, Report);
}

static VOID
Pv1dLogBufferedOutputMeta(
    _In_ PDEVICE_CONTEXT Ctx,
    _In_ LONG64 CycleId,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report,
    _In_ ULONG_PTR Information
    )
{
    USHORT scanTime;
    ULONG validSlotCount;
    ULONG activeTipCount;
    ULONG tipMask;
    ULONG confidenceMask;

    scanTime = (USHORT)(
        ((USHORT)Report[PV1B_SCAN_TIME_LOW_OFFSET]) |
        (((USHORT)Report[PV1B_SCAN_TIME_HIGH_OFFSET]) << 8)
        );

    Pv1bComputeTipAudit(
        Report,
        &validSlotCount,
        &activeTipCount,
        &tipMask,
        &confidenceMask
        );

    DbgPrint(
        "PalmRejPipeline PV1I: ROLL-POP-OLDEST cycle=%I64d dev=%ld info=%Iu reportId=0x%02X contactCount=%u scanTime=%u validSlotCount=%lu activeTipCount=%lu tipMask=0x%03lX confidenceMask=0x%03lX\n",
        CycleId,
        Ctx->DeviceId,
        Information,
        (ULONG)Report[0],
        (ULONG)Report[PV1B_CONTACT_COUNT_OFFSET],
        (ULONG)scanTime,
        validSlotCount,
        activeTipCount,
        tipMask,
        confidenceMask
        );
}

static VOID
Pv1bLogLookaheadRequestContext(
    _In_z_ PCSTR Event,
    _In_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _In_ LONG64 CycleId,
    _In_ ULONG PhysicalIndex,
    _In_ LONG Phase,
    _In_ NTSTATUS Status
    )
{
    PIRP irp;
    PFILE_OBJECT originalFileObject;
    PFILE_OBJECT currentFileObject;
    PFILE_OBJECT nextFileObject;
    KPROCESSOR_MODE requestorMode;
    BOOLEAN canceled;
    BOOLEAN irpCancel;

    irp = WdfRequestWdmGetIrp(Request);
    originalFileObject = Pv1bGetOriginalFileObject(Request);
    currentFileObject = Pv1bGetCurrentFileObject(Request);
    nextFileObject = Pv1bGetNextFileObject(Request);
    requestorMode = WdfRequestGetRequestorMode(Request);
    canceled = WdfRequestIsCanceled(Request);
    irpCancel = (irp != NULL && irp->Cancel) ? TRUE : FALSE;

    DbgPrint(
        "PalmRejPipeline PV1I: %s cycle=%I64d dev=%ld physicalIndex=%lu phase=%ld status=0x%08X canceled=%ld irpCancel=%ld reqMode=%ld irp=%p originalFO=%p currentFO=%p nextFO=%p\n",
        Event,
        CycleId,
        Ctx->DeviceId,
        PhysicalIndex,
        Phase,
        (ULONG)Status,
        canceled ? 1L : 0L,
        irpCancel ? 1L : 0L,
        (LONG)requestorMode,
        irp,
        originalFileObject,
        currentFileObject,
        nextFileObject
        );
}

static BOOLEAN
Pv1bIsActiveTouchReport(
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Report
    )
{
    ULONG validSlotCount;
    ULONG activeTipCount;
    ULONG tipMask;
    ULONG confidenceMask;

    if (Report[0] != PV1B_REPORT_ID_TOUCH) {
        return FALSE;
    }

    Pv1bComputeTipAudit(
        Report,
        &validSlotCount,
        &activeTipCount,
        &tipMask,
        &confidenceMask
        );

    UNREFERENCED_PARAMETER(validSlotCount);
    UNREFERENCED_PARAMETER(tipMask);
    UNREFERENCED_PARAMETER(confidenceMask);

    // PV1D verified boundary rule inherited from PV1B:
    // Contact Count can remain 1 on a release report (observed state 0x02).
    // Episode activity therefore follows actual per-slot Tip state.
    return (activeTipCount != 0u) ? TRUE : FALSE;
}

static VOID
Pv1bCompleteUpper(
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx,
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ NTSTATUS Status,
    _In_ ULONG_PTR Information,
    _In_z_ PCSTR Event
    )
{
    LONG previous;

    previous = InterlockedCompareExchange(
        &RequestCtx->UpperCompletionIssued,
        1,
        0
        );

    if (previous != 0) {
        InterlockedIncrement64(
            &Ctx->Counters.DoubleCompletePrevented
            );

        // PV0112 sticky: a second completion of one request means request
        // lifetime tracking is broken; keep the PV1K latch.
        Pv0112EnterBypass(
            Ctx,
            "DOUBLE-COMPLETE-PREVENTED",
            RequestCtx->CycleId,
            TRUE
            );

        Pv1bLogEvent(
            "DOUBLE-COMPLETE-PREVENTED",
            Ctx,
            RequestCtx->CycleId,
            previous,
            Status
            );

        return;
    }

    InterlockedIncrement64(
        &Ctx->Counters.UpperCompletions
        );

    if (Status == STATUS_CANCELLED) {
        InterlockedIncrement64(
            &Ctx->Counters.UpperCanceledCompletions
            );
    }

    Pv1jRememberUpperVisibleTouch(
        Ctx,
        Request,
        Status,
        Information
        );

    Pv1bLogEvent(
        Event,
        Ctx,
        RequestCtx->CycleId,
        Status,
        Information
        );

    {
        WDFDEVICE device;
        LONG releaseDeviceReference;

        device = RequestCtx->Device;

        releaseDeviceReference = InterlockedExchange(
            &RequestCtx->DeviceReferenceHeld,
            0
            );

        WdfRequestCompleteWithInformation(
            Request,
            Status,
            Information
            );

        if (releaseDeviceReference != 0) {
            WdfObjectDereference(
                device
                );
        }
    }
}

static BOOLEAN
Pv1dTryRollingExchange(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* NewReport,
    _In_ ULONG_PTR NewInformation
    )
{
    UCHAR outputReport[PV1B_REPORT_BYTES];
    UCHAR lastUpperTouchReport[PV1B_REPORT_BYTES];
    ULONG_PTR outputInformation;
    LONG count;
    ULONG head;
    ULONG tail;
    ULONG index;
    ULONG lastUpperValidSlotCount;
    ULONG lastUpperActiveTipCount;
    ULONG lastUpperTipMask;
    ULONG lastUpperConfidenceMask;
    ULONG physicalValidSlotCount;
    ULONG physicalActiveTipCount;
    ULONG physicalTipMask;
    ULONG physicalConfidenceMask;
    ULONG physicalReportedCount;
    BOOLEAN haveOutput;
    BOOLEAN copied;
    BOOLEAN canceledWithFifo;
    BOOLEAN boundaryFlush;
    BOOLEAN coherentExact2BoundaryRelease;
    BOOLEAN coherentExact1ZeroBoundaryRelease;
    BOOLEAN stalePurgeBeforeNewEpisode;
    BOOLEAN invalidFifo;
    BOOLEAN newReportActive;
    BOOLEAN bufferedNonactiveFound;
    BOOLEAN futureVetoSuppressOutput;
    BOOLEAN lastUpperTouchValid;
    LONG futureVetoReasonMask;
    LONG droppedActiveCount;
    LONG clearedEpisodeState;
    LARGE_INTEGER now;
    LARGE_INTEGER frequency;
    LONG64 previousQpc;
    LONG64 deltaQpc;
    LONG64 ticksPerMicrosecond;
    LONG64 gapUs;

    RtlZeroMemory(outputReport, sizeof(outputReport));
    RtlZeroMemory(lastUpperTouchReport, sizeof(lastUpperTouchReport));
    outputInformation = 0u;
    count = 0;
    head = 0u;
    tail = 0u;
    lastUpperValidSlotCount = 0u;
    lastUpperActiveTipCount = 0u;
    lastUpperTipMask = 0u;
    lastUpperConfidenceMask = 0u;
    physicalValidSlotCount = 0u;
    physicalActiveTipCount = 0u;
    physicalTipMask = 0u;
    physicalConfidenceMask = 0u;
    physicalReportedCount = 0u;
    haveOutput = FALSE;
    canceledWithFifo = FALSE;
    boundaryFlush = FALSE;
    coherentExact2BoundaryRelease = FALSE;
    coherentExact1ZeroBoundaryRelease = FALSE;
    stalePurgeBeforeNewEpisode = FALSE;
    invalidFifo = FALSE;
    newReportActive = Pv1bIsActiveTouchReport(NewReport);
    bufferedNonactiveFound = FALSE;
    futureVetoSuppressOutput = FALSE;
    lastUpperTouchValid = FALSE;
    futureVetoReasonMask = 0;
    droppedActiveCount = 0;
    clearedEpisodeState = 0;
    deltaQpc = 0;
    ticksPerMicrosecond = 0;
    gapUs = -1;

    WdfSpinLockAcquire(Ctx->StateLock);

    count = Ctx->DeferredValid;
    futureVetoReasonMask = Ctx->FutureVetoReasonMask;

    if (Ctx->LastUpperTouchValid != 0) {
        RtlCopyMemory(
            lastUpperTouchReport,
            Ctx->LastUpperTouchReport,
            PV1B_REPORT_BYTES
            );
        lastUpperTouchValid = TRUE;
    }

    if (count == 0) {
        WdfSpinLockRelease(Ctx->StateLock);
        return FALSE;
    }

    InterlockedIncrement64(&Ctx->Counters.RollingPhysicalArrivals);

    if (WdfRequestIsCanceled(Request)) {
        InterlockedExchange(&Ctx->DeferredValid, 0);
        InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
        Ctx->DeferredHead = 0u;
        clearedEpisodeState = InterlockedExchange(
            &Ctx->PhysicalEpisodeActive,
            0
            );
        InterlockedExchange(&Ctx->FutureVetoActive, 0);
        InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
        canceledWithFifo = TRUE;
    }
    else if (count > 0 && count <= (LONG)PV1B_DEFERRED_SLOTS) {
        head = Ctx->DeferredHead;

        if (head < PV1B_DEFERRED_SLOTS) {
            for (index = 0u;
                 index < (ULONG)count;
                 index++) {
                ULONG scanIndex;

                scanIndex = (head + index) % PV1B_DEFERRED_SLOTS;

                if (Pv1bIsActiveTouchReport(
                        Ctx->DeferredReport[scanIndex]
                        )) {
                    droppedActiveCount++;
                }
                else {
                    bufferedNonactiveFound = TRUE;
                }
            }

            if (!newReportActive) {
                /*
                 * PV1J-LAST-UPPER-VISIBLE-EXACT2-BOUNDARY-RELEASE-V1
                 *
                 * DeferredHead is the NEXT delayed report, not the last report
                 * HIDClass already saw. Use only the exact cached bytes from
                 * the most recent successful upper TOUCH completion.
                 *
                 * If HIDClass still sees two fully confident active contacts
                 * while the real physical stream has reached a final
                 * non-active boundary with at most one reported contact, the
                 * FIFO hid the intermediate first-contact UP. Emit one
                 * coherent two-contact release using the visible IDs and
                 * coordinates, but the real boundary scan time.
                 *
                 * If an intermediate 2F->1F release already reached HIDClass,
                 * the cache has only one active Tip and this gate stays off.
                 * Already-coherent 02,02/count2 releases from PalmRejFilter
                 * v28EU/v28EV also stay unchanged because physical count=2.
                 */
                if (lastUpperTouchValid &&
                    lastUpperTouchReport[0] == PV1B_REPORT_ID_TOUCH) {
                    Pv1bComputeTipAudit(
                        lastUpperTouchReport,
                        &lastUpperValidSlotCount,
                        &lastUpperActiveTipCount,
                        &lastUpperTipMask,
                        &lastUpperConfidenceMask
                        );
                }

                if (NewReport[0] == PV1B_REPORT_ID_TOUCH) {
                    Pv1bComputeTipAudit(
                        NewReport,
                        &physicalValidSlotCount,
                        &physicalActiveTipCount,
                        &physicalTipMask,
                        &physicalConfidenceMask
                        );
                    physicalReportedCount =
                        (ULONG)NewReport[PV1B_CONTACT_COUNT_OFFSET];
                }

                UNREFERENCED_PARAMETER(physicalTipMask);
                UNREFERENCED_PARAMETER(physicalConfidenceMask);

                if (droppedActiveCount > 0 &&
                    lastUpperTouchValid &&
                    lastUpperTouchReport[0] == PV1B_REPORT_ID_TOUCH &&
                    lastUpperValidSlotCount == 2u &&
                    lastUpperActiveTipCount == 2u &&
                    (ULONG)lastUpperTouchReport[PV1B_CONTACT_COUNT_OFFSET] == 2u &&
                    lastUpperTipMask == lastUpperConfidenceMask &&
                    NewReport[0] == PV1B_REPORT_ID_TOUCH &&
                    physicalActiveTipCount == 0u &&
                    physicalReportedCount <= 1u &&
                    (physicalReportedCount == 0u ||
                     physicalValidSlotCount <= 1u)) {
                    ULONG slot;

                    RtlCopyMemory(
                        outputReport,
                        lastUpperTouchReport,
                        PV1B_REPORT_BYTES
                        );

                    for (slot = 0u;
                         slot < PV1B_CONTACT_SLOT_COUNT;
                         ++slot) {
                        ULONG stateOffset;
                        UCHAR state;

                        stateOffset =
                            PV1B_CONTACT_SLOT_BASE_OFFSET +
                            (slot * PV1B_CONTACT_SLOT_BYTES);
                        state = outputReport[stateOffset];

                        if (state != PV1B_STATE_UNUSED_SENTINEL) {
                            outputReport[stateOffset] =
                                (UCHAR)(state & ~PV1B_STATE_TIP_BIT);
                        }
                    }

                    outputReport[PV1B_SCAN_TIME_LOW_OFFSET] =
                        NewReport[PV1B_SCAN_TIME_LOW_OFFSET];
                    outputReport[PV1B_SCAN_TIME_HIGH_OFFSET] =
                        NewReport[PV1B_SCAN_TIME_HIGH_OFFSET];
                    outputReport[PV1B_CONTACT_COUNT_OFFSET] = 0x02;

                    coherentExact2BoundaryRelease = TRUE;
                }
                else if (droppedActiveCount > 0 &&
                    lastUpperTouchValid &&
                    lastUpperTouchReport[0] == PV1B_REPORT_ID_TOUCH &&
                    lastUpperValidSlotCount == 1u &&
                    lastUpperActiveTipCount == 1u &&
                    (ULONG)lastUpperTouchReport[PV1B_CONTACT_COUNT_OFFSET] == 1u &&
                    lastUpperTipMask == lastUpperConfidenceMask &&
                    NewReport[0] == PV1B_REPORT_ID_TOUCH &&
                    physicalActiveTipCount == 0u &&
                    physicalReportedCount == 0u) {
                    ULONG slot;

                    /*
                     * PV1K-LAST-UPPER-VISIBLE-EXACT1-ZERO-BOUNDARY-RELEASE-V1
                     *
                     * Narrowly repair only the zero-contact boundary case.
                     * A normal physical 02/count1 release never matches this
                     * gate and remains byte-for-byte PV1J behavior.
                     */
                    RtlCopyMemory(
                        outputReport,
                        lastUpperTouchReport,
                        PV1B_REPORT_BYTES
                        );

                    for (slot = 0u;
                         slot < PV1B_CONTACT_SLOT_COUNT;
                         ++slot) {
                        ULONG stateOffset;
                        UCHAR state;

                        stateOffset =
                            PV1B_CONTACT_SLOT_BASE_OFFSET +
                            (slot * PV1B_CONTACT_SLOT_BYTES);
                        state = outputReport[stateOffset];

                        if (state != PV1B_STATE_UNUSED_SENTINEL) {
                            outputReport[stateOffset] =
                                (UCHAR)(state & ~PV1B_STATE_TIP_BIT);
                        }
                    }

                    outputReport[PV1B_SCAN_TIME_LOW_OFFSET] =
                        NewReport[PV1B_SCAN_TIME_LOW_OFFSET];
                    outputReport[PV1B_SCAN_TIME_HIGH_OFFSET] =
                        NewReport[PV1B_SCAN_TIME_HIGH_OFFSET];
                    outputReport[PV1B_CONTACT_COUNT_OFFSET] = 0x01;

                    coherentExact1ZeroBoundaryRelease = TRUE;
                }
                else {
                    // Preserve exact PV1J behavior outside the two narrow
                    // last-upper-visible boundary repairs.
                    RtlCopyMemory(
                        outputReport,
                        NewReport,
                        PV1B_REPORT_BYTES
                        );
                }

                outputInformation = NewInformation;
                InterlockedExchange(&Ctx->DeferredValid, 0);
                InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
                Ctx->DeferredHead = 0u;
                clearedEpisodeState = InterlockedExchange(
                    &Ctx->PhysicalEpisodeActive,
                    0
                    );
                InterlockedExchange(&Ctx->FutureVetoActive, 0);
                InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
                InterlockedIncrement64(
                    &Ctx->Counters.RollingAllNonactiveClears
                    );
                InterlockedIncrement64(
                    &Ctx->Counters.RollingBoundaryFlushes
                    );
                if (droppedActiveCount > 0) {
                    InterlockedExchangeAdd64(
                        &Ctx->Counters.BoundaryActiveTailDrops,
                        droppedActiveCount
                        );
                }
                boundaryFlush = TRUE;
                haveOutput = TRUE;
            }
            else if (Ctx->PhysicalEpisodeActive == 0 ||
                     Ctx->BoundaryReleasePending != 0 ||
                     bufferedNonactiveFound) {
                // A new active physical report must never consume bytes from
                // a closed episode. Purge the old FIFO, then let this same
                // physical report enter the ordinary fresh-episode path.
                InterlockedExchange(&Ctx->DeferredValid, 0);
                InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
                Ctx->DeferredHead = 0u;
                clearedEpisodeState = InterlockedExchange(
                    &Ctx->PhysicalEpisodeActive,
                    0
                    );
                InterlockedExchange(&Ctx->FutureVetoActive, 0);
                InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
                stalePurgeBeforeNewEpisode = TRUE;
            }
            else {
                RtlCopyMemory(
                    outputReport,
                    Ctx->DeferredReport[head],
                    PV1B_REPORT_BYTES
                    );

                outputInformation = Ctx->DeferredInformation[head];
                tail = (head + (ULONG)count) % PV1B_DEFERRED_SLOTS;

                RtlCopyMemory(
                    Ctx->DeferredReport[tail],
                    NewReport,
                    PV1B_REPORT_BYTES
                    );

                Ctx->DeferredInformation[tail] = NewInformation;
                Ctx->DeferredHead =
                    (head + 1u) % PV1B_DEFERRED_SLOTS;

                if (Ctx->FutureVetoActive != 0) {
                    futureVetoSuppressOutput = TRUE;
                }

                haveOutput = TRUE;
            }
        }
        else {
            InterlockedExchange(&Ctx->DeferredValid, 0);
            InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
            Ctx->DeferredHead = 0u;
            clearedEpisodeState = InterlockedExchange(
                &Ctx->PhysicalEpisodeActive,
                0
                );
            InterlockedExchange(&Ctx->FutureVetoActive, 0);
            InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
            invalidFifo = TRUE;
        }
    }
    else if (count < 0 || count > (LONG)PV1B_DEFERRED_SLOTS) {
        InterlockedExchange(&Ctx->DeferredValid, 0);
        InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
        Ctx->DeferredHead = 0u;
        clearedEpisodeState = InterlockedExchange(
            &Ctx->PhysicalEpisodeActive,
            0
            );
        InterlockedExchange(&Ctx->FutureVetoActive, 0);
        InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
        invalidFifo = TRUE;
    }

    WdfSpinLockRelease(Ctx->StateLock);

    if (canceledWithFifo) {
        InterlockedIncrement64(&Ctx->Counters.RollingInvariantFailures);
        InterlockedIncrement64(&Ctx->Counters.FatalBypassEvents);
        Pv0112EnterBypass(Ctx, "ROLL-CANCELED-WITH-FIFO", RequestCtx->CycleId, FALSE);
        InterlockedExchange64(&Ctx->LastRollingOutputQpc, 0);

        if (clearedEpisodeState != 0) {
            InterlockedIncrement64(&Ctx->Counters.EpisodeEnds);
        }

        Pv1bLogEvent(
            "ROLL-CANCELED-CLEAR-EPISODE-END",
            Ctx,
            RequestCtx->CycleId,
            count,
            clearedEpisodeState
            );

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_CANCELLED,
            0u,
            "ROLL-CANCELED-UPPER"
            );

        return TRUE;
    }

    if (stalePurgeBeforeNewEpisode) {
        InterlockedIncrement64(
            &Ctx->Counters.StaleFifoPurgesBeforeNewEpisode
            );
        InterlockedExchange64(&Ctx->LastRollingOutputQpc, 0);

        if (clearedEpisodeState != 0) {
            InterlockedIncrement64(&Ctx->Counters.EpisodeEnds);
        }

        Pv1bLogEvent(
            "BOUNDARY-STALE-FIFO-PURGED-BEFORE-NEW-EPISODE",
            Ctx,
            RequestCtx->CycleId,
            count,
            droppedActiveCount
            );

        return FALSE;
    }

    if (invalidFifo || !haveOutput) {
        InterlockedIncrement64(
            &Ctx->Counters.RollingInvariantFailures
            );
        InterlockedIncrement64(&Ctx->Counters.FatalBypassEvents);
        Pv0112EnterBypass(Ctx, "ROLL-INVARIANT-FAIL", RequestCtx->CycleId, FALSE);
        InterlockedExchange64(&Ctx->LastRollingOutputQpc, 0);

        if (clearedEpisodeState != 0) {
            InterlockedIncrement64(&Ctx->Counters.EpisodeEnds);
        }

        Pv1bLogEvent(
            "ROLL-INVARIANT-CLEAR-EPISODE-END",
            Ctx,
            RequestCtx->CycleId,
            count,
            clearedEpisodeState
            );

        Pv1bLogEvent(
            "ROLL-INVARIANT-FAIL",
            Ctx,
            RequestCtx->CycleId,
            count,
            head
            );

        return FALSE;
    }

    if (boundaryFlush) {
        if (clearedEpisodeState != 0) {
            InterlockedIncrement64(&Ctx->Counters.EpisodeEnds);
        }

        Pv1bLogEvent(
            "BOUNDARY-PHYSICAL-NONACTIVE",
            Ctx,
            RequestCtx->CycleId,
            count,
            droppedActiveCount
            );

        Pv1bLogEvent(
            "BOUNDARY-DROP-ACTIVE-TAIL",
            Ctx,
            RequestCtx->CycleId,
            droppedActiveCount,
            count
            );

        Pv1bLogEvent(
            "BOUNDARY-EPISODE-END",
            Ctx,
            RequestCtx->CycleId,
            count,
            clearedEpisodeState
            );

        if (coherentExact2BoundaryRelease) {
            DbgPrint(
                "PalmRejPipeline PV1J-LAST-UPPER-VISIBLE-EXACT2-BOUNDARY-RELEASE-V1: SYNTH-COHERENT-DUAL-UP cycle=%I64d dev=%ld visibleTipMask=0x%03lX physicalCount=%lu action=RELEASE-ONLY\n",
                RequestCtx->CycleId,
                Ctx->DeviceId,
                lastUpperTipMask,
                physicalReportedCount
                );
        }
        if (coherentExact1ZeroBoundaryRelease) {
            DbgPrint(
                "PalmRejPipeline PV1K-LAST-UPPER-VISIBLE-EXACT1-ZERO-BOUNDARY-RELEASE-V1: SYNTH-COHERENT-ONE-UP cycle=%I64d dev=%ld visibleTipMask=0x%03lX physicalCount=%lu action=RELEASE-ONLY\n",
                RequestCtx->CycleId,
                Ctx->DeviceId,
                lastUpperTipMask,
                physicalReportedCount
                );
        }
    }

    if (futureVetoSuppressOutput && !boundaryFlush) {
        Pv1eBuildZeroTouchReport(outputReport);
        InterlockedIncrement64(
            &Ctx->Counters.FutureVetoSuppressedRollingReports
            );

        DbgPrint(
            "PalmRejPipeline PV1I: FUTURE-VETO-ROLL-SUPPRESS cycle=%I64d dev=%ld reason=0x%02lX\n",
            RequestCtx->CycleId,
            Ctx->DeviceId,
            (ULONG)futureVetoReasonMask
            );
    }

    copied = Pv1bCopyReportToRequest(Request, outputReport);

    if (!copied) {
        InterlockedIncrement64(&Ctx->Counters.RollingInvariantFailures);
        InterlockedIncrement64(&Ctx->Counters.FatalBypassEvents);
        Pv0112EnterBypass(Ctx, "ROLL-COPY-TO-UPPER-FAIL", RequestCtx->CycleId, FALSE);

        WdfSpinLockAcquire(Ctx->StateLock);
        InterlockedExchange(&Ctx->DeferredValid, 0);
        InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
        Ctx->DeferredHead = 0u;
        clearedEpisodeState = InterlockedExchange(
            &Ctx->PhysicalEpisodeActive,
            0
            );
        InterlockedExchange(&Ctx->FutureVetoActive, 0);
        InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
        WdfSpinLockRelease(Ctx->StateLock);

        InterlockedExchange64(&Ctx->LastRollingOutputQpc, 0);

        if (clearedEpisodeState != 0) {
            InterlockedIncrement64(&Ctx->Counters.EpisodeEnds);
        }

        Pv1bLogEvent(
            "ROLL-COPY-FAIL-CLEAR-EPISODE-END",
            Ctx,
            RequestCtx->CycleId,
            count,
            clearedEpisodeState
            );

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_DATA_ERROR,
            0u,
            "ROLL-COPY-TO-UPPER-FAIL"
            );

        return TRUE;
    }

    now = KeQueryPerformanceCounter(&frequency);
    previousQpc = InterlockedExchange64(
        &Ctx->LastRollingOutputQpc,
        now.QuadPart
        );

    if (previousQpc != 0 &&
        now.QuadPart >= previousQpc &&
        frequency.QuadPart > 0) {
        deltaQpc = now.QuadPart - previousQpc;
        ticksPerMicrosecond = frequency.QuadPart / 1000000LL;

        if (ticksPerMicrosecond > 0) {
            gapUs = deltaQpc / ticksPerMicrosecond;
        }

        if (frequency.QuadPart >= 1000LL &&
            deltaQpc < (frequency.QuadPart / 1000LL)) {
            InterlockedIncrement64(
                &Ctx->Counters.RollingCadenceViolations
                );
        }
    }

    InterlockedIncrement64(&Ctx->Counters.RollingOutputs);

    Pv1bLogEvent(
        "ROLL-PHYSICAL-ARRIVE",
        Ctx,
        RequestCtx->CycleId,
        NewInformation,
        count
        );

    if (boundaryFlush) {
        Pv1bLogReportMeta(
            "BOUNDARY-DIRECT-RELEASE",
            Ctx,
            RequestCtx->CycleId,
            outputReport,
            outputInformation
            );
    }
    else {
        Pv1dLogBufferedOutputMeta(
            Ctx,
            RequestCtx->CycleId,
            outputReport,
            outputInformation
            );

        Pv1bLogEvent(
            "ROLL-PUSH-NEWEST",
            Ctx,
            RequestCtx->CycleId,
            count,
            tail
            );
    }

    Pv1bLogEvent(
        "ROLL-UPPER-COMPLETE",
        Ctx,
        RequestCtx->CycleId,
        gapUs,
        count
        );

    if (boundaryFlush) {
        (VOID)InterlockedCompareExchange64(
            &Ctx->LastRollingOutputQpc,
            0,
            now.QuadPart
            );
    }

    Pv1bCompleteUpper(
        Request,
        RequestCtx,
        Ctx,
        STATUS_SUCCESS,
        outputInformation,
        boundaryFlush ?
            "BOUNDARY-DIRECT-RELEASE-COMPLETE" :
            "ROLL-ONE-IN-ONE-OUT-COMPLETE"
        );

    return TRUE;
}

static VOID
Pv1bClearActiveLookaheadReference(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request
    )
{
    BOOLEAN releaseReference;

    releaseReference = FALSE;

    WdfSpinLockAcquire(
        Ctx->StateLock
        );

    if (Ctx->ActiveLookaheadRequest == Request) {
        Ctx->ActiveLookaheadRequest = WDF_NO_HANDLE;
        releaseReference = TRUE;
    }

    WdfSpinLockRelease(
        Ctx->StateLock
        );

    if (releaseReference) {
        WdfObjectDereference(
            Request
            );
    }
}

static BOOLEAN
Pv1bPublishActiveLookaheadReference(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request
    )
{
    WdfObjectReference(
        Request
        );

    WdfSpinLockAcquire(
        Ctx->StateLock
        );

    if (Ctx->ActiveLookaheadRequest != WDF_NO_HANDLE) {
        WdfSpinLockRelease(
            Ctx->StateLock
            );

        WdfObjectDereference(
            Request
            );

        // PV0112 sticky: two requests in the lookahead at once means the
        // reader keeps more than one read pending, which one FIFO cannot
        // serve; resuming would only collide again.  Keep the PV1K latch.
        Pv0112EnterBypass(
            Ctx,
            "ACTIVE-LOOKAHEAD-COLLISION",
            0,
            TRUE
            );

        InterlockedIncrement64(
            &Ctx->Counters.FatalBypassEvents
            );

        Pv1bLogEvent(
            "ACTIVE-LOOKAHEAD-COLLISION",
            Ctx,
            0,
            0,
            0
            );

        return FALSE;
    }

    Ctx->ActiveLookaheadRequest = Request;

    WdfSpinLockRelease(
        Ctx->StateLock
        );

    return TRUE;
}

static VOID
Pv1bCancelActiveLookahead(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_z_ PCSTR Event
    )
{
    WDFREQUEST request;
    BOOLEAN cancelAccepted;

    request = WDF_NO_HANDLE;

    WdfSpinLockAcquire(
        Ctx->StateLock
        );

    if (Ctx->ActiveLookaheadRequest != WDF_NO_HANDLE) {
        request = Ctx->ActiveLookaheadRequest;

        WdfObjectReference(
            request
            );
    }

    WdfSpinLockRelease(
        Ctx->StateLock
        );

    if (request == WDF_NO_HANDLE) {
        Pv1bLogEvent(
            Event,
            Ctx,
            0,
            0,
            0
            );

        return;
    }

    InterlockedIncrement64(
        &Ctx->Counters.CancelSentAttempts
        );

    cancelAccepted = WdfRequestCancelSentRequest(
        request
        );

    if (cancelAccepted) {
        InterlockedIncrement64(
            &Ctx->Counters.CancelSentAccepted
            );
    }
    else {
        InterlockedIncrement64(
            &Ctx->Counters.CancelSentRejected
            );
    }

    Pv1bLogEvent(
        Event,
        Ctx,
        cancelAccepted ? 1 : 0,
        1,
        0
        );

    WdfObjectDereference(
        request
        );
}

static VOID
Pv1bForwardDirect(
    _In_ WDFDEVICE Device,
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _In_z_ PCSTR FailureEvent
    )
{
    WDF_REQUEST_SEND_OPTIONS options;
    NTSTATUS status;
    BOOLEAN sent;

    WdfRequestFormatRequestUsingCurrentType(
        Request
        );

    WDF_REQUEST_SEND_OPTIONS_INIT(
        &options,
        WDF_REQUEST_SEND_OPTION_SEND_AND_FORGET
        );

    sent = WdfRequestSend(
        Request,
        WdfDeviceGetIoTarget(Device),
        &options
        );

    if (sent == FALSE) {
        status = WdfRequestGetStatus(
            Request
            );

        InterlockedIncrement64(
            &Ctx->Counters.DirectForwardFailures
            );

        WdfRequestComplete(
            Request,
            status
            );

        Pv1bLogEvent(
            FailureEvent,
            Ctx,
            status,
            0,
            0
            );

        return;
    }

    InterlockedIncrement64(
        &Ctx->Counters.DirectForwards
        );
}


static BOOLEAN
Pv1bEnsureEpisodeStartInvariant(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ LONG64 CycleId
    )
{
    LONG previousEpisodeState;
    LONG deferredCount;
    LONG64 episodeId;
    BOOLEAN startAccepted;

    previousEpisodeState = 0;
    deferredCount = 0;
    startAccepted = FALSE;

    WdfSpinLockAcquire(
        Ctx->StateLock
        );

    previousEpisodeState = Ctx->PhysicalEpisodeActive;

    if (previousEpisodeState != 0) {
        WdfSpinLockRelease(
            Ctx->StateLock
            );

        return TRUE;
    }

    deferredCount = Ctx->DeferredValid;

    if (deferredCount == 0 &&
        Ctx->BoundaryReleasePending == 0) {
        RtlZeroMemory(
            Ctx->LastUpperTouchReport,
            PV1B_REPORT_BYTES
            );
        Ctx->LastUpperTouchInformation = 0u;
        InterlockedExchange(&Ctx->LastUpperTouchValid, 0);
        InterlockedExchange(&Ctx->FutureVetoActive, 0);
        InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
        InterlockedExchange(
            &Ctx->PhysicalEpisodeActive,
            1
            );

        startAccepted = TRUE;
    }

    WdfSpinLockRelease(
        Ctx->StateLock
        );

    if (!startAccepted) {
        InterlockedIncrement64(
            &Ctx->Counters.EpisodeStartInvariantFailures
            );

        InterlockedIncrement64(
            &Ctx->Counters.FatalBypassEvents
            );

        Pv0112EnterBypass(
            Ctx,
            "EPISODE-START-FIFO-NOT-EMPTY",
            CycleId,
            FALSE
            );

        Pv1bLogEvent(
            "EPISODE-START-FIFO-NOT-EMPTY-HARD-FAIL",
            Ctx,
            CycleId,
            deferredCount,
            0
            );

        return FALSE;
    }

    episodeId = InterlockedIncrement64(
        &Ctx->EpisodeId
        );

    InterlockedIncrement64(
        &Ctx->Counters.EpisodeStarts
        );

    Pv1bLogEvent(
        "EPISODE-START-FIFO-EMPTY",
        Ctx,
        CycleId,
        episodeId,
        0
        );

    return TRUE;
}

static VOID
Pv1bMarkEpisodeEnd(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ LONG64 CycleId,
    _In_ LONG TailCount,
    _In_z_ PCSTR Event
    )
{
    LONG previousEpisodeState;

    previousEpisodeState = InterlockedExchange(
        &Ctx->PhysicalEpisodeActive,
        0
        );
    InterlockedExchange(&Ctx->FutureVetoActive, 0);
    InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);

    if (previousEpisodeState != 0) {
        InterlockedIncrement64(
            &Ctx->Counters.EpisodeEnds
            );
    }

    Pv1bLogEvent(
        Event,
        Ctx,
        CycleId,
        TailCount,
        previousEpisodeState
        );
}

static BOOLEAN
Pv1bStoreDeferredTail(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ PPV1B_REQUEST_CONTEXT RequestCtx,
    _In_ LONG Count,
    _In_ LONG64 CycleId
    )
{
    BOOLEAN collision;
    LONG i;

    if (Count < 1 ||
        Count > (LONG)PV1B_DEFERRED_SLOTS ||
        RequestCtx->StoredLookaheadCount < (ULONG)Count) {
        InterlockedIncrement64(
            &Ctx->Counters.FatalBypassEvents
            );

        Pv0112EnterBypass(
            Ctx,
            "DEFERRED-TAIL-INVALID-COUNT",
            CycleId,
            FALSE
            );

        Pv1bLogEvent(
            "DEFERRED-TAIL-INVALID-COUNT-HARD-FAIL",
            Ctx,
            CycleId,
            Count,
            RequestCtx->StoredLookaheadCount
            );

        return FALSE;
    }

    collision = FALSE;

    WdfSpinLockAcquire(
        Ctx->StateLock
        );

    if (Ctx->DeferredValid != 0 ||
        Ctx->BoundaryReleasePending != 0) {
        collision = TRUE;
    }
    else {
        for (i = 0; i < Count; ++i) {
            RtlCopyMemory(
                Ctx->DeferredReport[i],
                RequestCtx->LookaheadReport[i],
                PV1B_REPORT_BYTES
                );

            Ctx->DeferredInformation[i] =
                RequestCtx->LookaheadInformation[i];
        }

        Ctx->DeferredHead = 0u;

        InterlockedExchange(
            &Ctx->DeferredValid,
            Count
            );

        InterlockedExchange(
            &Ctx->BoundaryReleasePending,
            0
            );
    }

    WdfSpinLockRelease(
        Ctx->StateLock
        );

    if (collision) {
        InterlockedIncrement64(
            &Ctx->Counters.DeferredCollisionFailures
            );

        InterlockedIncrement64(
            &Ctx->Counters.FatalBypassEvents
            );

        Pv0112EnterBypass(
            Ctx,
            "DEFERRED-COLLISION",
            CycleId,
            FALSE
            );

        Pv1bLogEvent(
            "DEFERRED-COLLISION-HARD-FAIL",
            Ctx,
            CycleId,
            Count,
            RequestCtx->StoredLookaheadCount
            );

        return FALSE;
    }

    for (i = 0; i < Count; ++i) {
        InterlockedIncrement64(
            &Ctx->Counters.DeferredStores
            );
    }

    Pv1bLogEvent(
        "DEFERRED-TAIL-STORED",
        Ctx,
        CycleId,
        Count,
        RequestCtx->StoredLookaheadCount
        );

    return TRUE;
}

/* 0.1.10: lookahead releases rebuilt from report 1 - see the lookahead completion. */
static volatile LONG64 g_V0110CoherentReleases = 0;

static BOOLEAN
Pv1dStoreBoundaryRelease(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* ReleaseReport,
    _In_ ULONG_PTR ReleaseInformation,
    _In_ LONG DroppedActiveTailCount,
    _In_ LONG64 CycleId
    )
{
    BOOLEAN collision;

    if (Pv1bIsActiveTouchReport(ReleaseReport) ||
        DroppedActiveTailCount < 0 ||
        DroppedActiveTailCount >= (LONG)PV1B_DEFERRED_SLOTS) {
        InterlockedIncrement64(
            &Ctx->Counters.BoundaryReleaseInvariantFailures
            );
        InterlockedIncrement64(&Ctx->Counters.FatalBypassEvents);
        Pv0112EnterBypass(Ctx, "BOUNDARY-RELEASE-STORE-INVALID", CycleId, FALSE);

        Pv1bLogEvent(
            "BOUNDARY-RELEASE-STORE-INVALID-HARD-FAIL",
            Ctx,
            CycleId,
            DroppedActiveTailCount,
            0
            );

        return FALSE;
    }

    collision = FALSE;

    WdfSpinLockAcquire(Ctx->StateLock);

    if (Ctx->DeferredValid != 0 ||
        Ctx->BoundaryReleasePending != 0) {
        collision = TRUE;
    }
    else {
        RtlCopyMemory(
            Ctx->DeferredReport[0],
            ReleaseReport,
            PV1B_REPORT_BYTES
            );
        Ctx->DeferredInformation[0] = ReleaseInformation;
        Ctx->DeferredHead = 0u;
        InterlockedExchange(&Ctx->DeferredValid, 1);
        InterlockedExchange(&Ctx->BoundaryReleasePending, 1);
    }

    WdfSpinLockRelease(Ctx->StateLock);

    if (collision) {
        InterlockedIncrement64(
            &Ctx->Counters.BoundaryReleaseInvariantFailures
            );
        InterlockedIncrement64(&Ctx->Counters.FatalBypassEvents);
        Pv0112EnterBypass(Ctx, "BOUNDARY-RELEASE-STORE-COLLISION", CycleId, FALSE);

        Pv1bLogEvent(
            "BOUNDARY-RELEASE-STORE-COLLISION-HARD-FAIL",
            Ctx,
            CycleId,
            Ctx->DeferredValid,
            Ctx->BoundaryReleasePending
            );

        return FALSE;
    }

    InterlockedIncrement64(&Ctx->Counters.DeferredStores);
    InterlockedIncrement64(&Ctx->Counters.BoundaryReleaseStores);
    InterlockedExchange64(&Ctx->LastRollingOutputQpc, 0);
    if (DroppedActiveTailCount > 0) {
        InterlockedExchangeAdd64(
            &Ctx->Counters.BoundaryActiveTailDrops,
            DroppedActiveTailCount
            );
    }

    Pv1bLogEvent(
        "BOUNDARY-RELEASE-STORED-AFTER-WARMUP",
        Ctx,
        CycleId,
        DroppedActiveTailCount,
        1
        );

    return TRUE;
}

static BOOLEAN
Pv1bRestoreFirstAndComplete(
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx,
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_z_ PCSTR Event
    )
{
    UCHAR zeroReport[PV1B_REPORT_BYTES];
    const UCHAR* reportToCopy;

    reportToCopy = RequestCtx->FirstReport;

    if (InterlockedCompareExchange(
            &RequestCtx->FutureVetoSuppressFirst,
            0,
            0
            ) != 0) {
        Pv1eBuildZeroTouchReport(zeroReport);
        reportToCopy = zeroReport;
        InterlockedIncrement64(
            &Ctx->Counters.FutureVetoSuppressedFirstReports
            );

        DbgPrint(
            "PalmRejPipeline PV1I: FUTURE-VETO-FIRST-SUPPRESS cycle=%I64d dev=%ld reason=0x%02lX firstPhysicalIndex=%lu\n",
            RequestCtx->CycleId,
            Ctx->DeviceId,
            RequestCtx->FutureVetoReasonMask,
            RequestCtx->FutureVetoFirstPhysicalIndex
            );
    }

    if (!Pv1bCopyReportToRequest(
            Request,
            reportToCopy
            )) {
        InterlockedIncrement64(
            &Ctx->Counters.DeferredBufferFailures
            );

        InterlockedIncrement64(
            &Ctx->Counters.FatalBypassEvents
            );

        Pv0112EnterBypass(
            Ctx,
            "FIRST-RESTORE-FAIL",
            RequestCtx->CycleId,
            FALSE
            );

        InterlockedExchange(
            &Ctx->DeferredValid,
            0
            );

        InterlockedExchange(
            &Ctx->BoundaryReleasePending,
            0
            );

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_DATA_ERROR,
            0u,
            "FIRST-RESTORE-FAIL"
            );

        return FALSE;
    }

    Pv1bCompleteUpper(
        Request,
        RequestCtx,
        Ctx,
        STATUS_SUCCESS,
        RequestCtx->FirstInformation,
        Event
        );

    return TRUE;
}

static VOID
Pv1bSendFirst(
    _In_ WDFDEVICE Device,
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx
    )
{
    BOOLEAN sent;
    NTSTATUS status;

    RequestCtx->Device = Device;
    RequestCtx->CycleId = InterlockedIncrement64(
        &g_Pv1bNextCycleId
        );
    RequestCtx->Phase = Pv1bRequestPhaseFirstSend;
    RequestCtx->UpperCompletionIssued = 0;
    RequestCtx->DeviceReferenceHeld = 0;
    RequestCtx->FirstInformation = 0u;
    RequestCtx->CurrentPhysicalIndex = 0u;
    RequestCtx->StoredLookaheadCount = 0u;
    RequestCtx->FutureVetoCandidate = 0;
    RequestCtx->FutureVetoSuppressFirst = 0;
    RequestCtx->FutureVetoReasonMask = 0u;
    RequestCtx->FutureVetoFirstPhysicalIndex = 0u;

    RtlZeroMemory(
        RequestCtx->FirstReport,
        sizeof(RequestCtx->FirstReport)
        );

    RtlZeroMemory(
        RequestCtx->LookaheadInformation,
        sizeof(RequestCtx->LookaheadInformation)
        );

    RtlZeroMemory(
        RequestCtx->LookaheadReport,
        sizeof(RequestCtx->LookaheadReport)
        );

    InterlockedIncrement64(
        &Ctx->Counters.CyclesStarted
        );

    WdfRequestFormatRequestUsingCurrentType(
        Request
        );

    WdfRequestSetCompletionRoutine(
        Request,
        EvtPv1bFirstCompletion,
        RequestCtx
        );

    WdfObjectReference(
        Device
        );

    InterlockedExchange(
        &RequestCtx->DeviceReferenceHeld,
        1
        );

    InterlockedIncrement64(
        &Ctx->Counters.FirstSendAttempts
        );

    Pv1bLogRequestContext(
        "FIRST-SEND",
        Ctx,
        Request,
        RequestCtx->CycleId,
        RequestCtx->Phase,
        STATUS_SUCCESS
        );

    sent = WdfRequestSend(
        Request,
        WdfDeviceGetIoTarget(Device),
        WDF_NO_SEND_OPTIONS
        );

    if (sent == FALSE) {
        status = WdfRequestGetStatus(
            Request
            );

        InterlockedIncrement64(
            &Ctx->Counters.FirstSendFailures
            );

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            status,
            0u,
            "FIRST-SEND-FAIL-UPPER-COMPLETE"
            );
    }
}

static NTSTATUS
Pv1bSendLookahead(
    _In_ WDFDEVICE Device,
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx,
    _In_ ULONG PhysicalIndex
    )
{
    WDF_REQUEST_REUSE_PARAMS reuseParams;
    NTSTATUS reuseStatus;
    NTSTATUS sendStatus;
    PFILE_OBJECT beforeOriginal;
    PFILE_OBJECT beforeCurrent;
    PFILE_OBJECT afterOriginal;
    PFILE_OBJECT afterCurrent;
    PFILE_OBJECT afterFormatNext;
    BOOLEAN sent;
    LONG previousPhase;

    if (PhysicalIndex < 2u ||
        PhysicalIndex > PV1B_TARGET_REPORT_DEPTH) {
        return STATUS_INVALID_PARAMETER;
    }

    if (WdfRequestIsCanceled(Request)) {
        return STATUS_CANCELLED;
    }

    beforeOriginal = Pv1bGetOriginalFileObject(
        Request
        );
    beforeCurrent = Pv1bGetCurrentFileObject(
        Request
        );

    if (!Pv1bIsNullFileObject(beforeOriginal) &&
        !Pv1bIsNullFileObject(beforeCurrent)) {
        InterlockedIncrement64(
            &Ctx->Counters.LookaheadFileObjectBeforeReuseNonNull
            );
    }

    RequestCtx->CurrentPhysicalIndex = PhysicalIndex;
    RequestCtx->Phase =
        Pv1bRequestPhaseLookaheadArmed;

    InterlockedIncrement64(
        &Ctx->Counters.LookaheadReuseArmed
        );

    Pv1bLogLookaheadRequestContext(
        "LOOKAHEAD-SEND-ARMED",
        Ctx,
        Request,
        RequestCtx->CycleId,
        PhysicalIndex,
        RequestCtx->Phase,
        STATUS_SUCCESS
        );

    WDF_REQUEST_REUSE_PARAMS_INIT(
        &reuseParams,
        WDF_REQUEST_REUSE_NO_FLAGS,
        STATUS_SUCCESS
        );

    InterlockedIncrement64(
        &Ctx->Counters.LookaheadReuseAttempts
        );

    reuseStatus = WdfRequestReuse(
        Request,
        &reuseParams
        );

    if (!NT_SUCCESS(reuseStatus)) {
        InterlockedIncrement64(
            &Ctx->Counters.LookaheadReuseFailures
            );

        Pv1bLogEvent(
            "LOOKAHEAD-REUSE-FAIL",
            Ctx,
            RequestCtx->CycleId,
            PhysicalIndex,
            reuseStatus
            );

        return reuseStatus;
    }

    InterlockedIncrement64(
        &Ctx->Counters.LookaheadReuseSuccesses
        );

    afterOriginal = Pv1bGetOriginalFileObject(
        Request
        );
    afterCurrent = Pv1bGetCurrentFileObject(
        Request
        );

    if (!Pv1bIsNullFileObject(afterOriginal) &&
        !Pv1bIsNullFileObject(afterCurrent)) {
        InterlockedIncrement64(
            &Ctx->Counters.LookaheadFileObjectAfterReuseNonNull
            );
    }

    WdfRequestFormatRequestUsingCurrentType(
        Request
        );

    afterFormatNext = Pv1bGetNextFileObject(
        Request
        );

    if (!Pv1bIsNullFileObject(afterFormatNext)) {
        InterlockedIncrement64(
            &Ctx->Counters.LookaheadFileObjectAfterFormatNonNull
            );
    }

    if (beforeOriginal != NULL &&
        beforeCurrent != NULL &&
        afterOriginal == beforeOriginal &&
        afterCurrent == beforeCurrent &&
        afterFormatNext == beforeCurrent) {
        InterlockedIncrement64(
            &Ctx->Counters.LookaheadFileObjectPreserved
            );
    }

    Pv1bLogLookaheadRequestContext(
        "LOOKAHEAD-SEND-POST-REUSE-FORMAT",
        Ctx,
        Request,
        RequestCtx->CycleId,
        PhysicalIndex,
        RequestCtx->Phase,
        reuseStatus
        );

    WdfRequestSetCompletionRoutine(
        Request,
        EvtPv1bLookaheadCompletion,
        RequestCtx
        );

    if (!Pv1bPublishActiveLookaheadReference(
            Ctx,
            Request
            )) {
        Pv1bLogEvent(
            "ACTIVE-LOOKAHEAD-COLLISION-LOOKAHEAD-SEND",
            Ctx,
            RequestCtx->CycleId,
            PhysicalIndex,
            0
            );

        return STATUS_DEVICE_BUSY;
    }

    InterlockedIncrement64(
        &Ctx->Counters.LookaheadSendArmed
        );

    InterlockedIncrement64(
        &Ctx->Counters.LookaheadSendAttempts
        );

    // PV1I UAF fix:
    // A lower target can complete an asynchronously sent request before
    // WdfRequestSend(TRUE) returns to this frame. Complete all RequestCtx
    // accesses and the phase transition before the send. On TRUE, return
    // immediately without touching Request or RequestCtx again.
    previousPhase = InterlockedCompareExchange(
        &RequestCtx->Phase,
        Pv1bRequestPhaseLookaheadInFlight,
        Pv1bRequestPhaseLookaheadArmed
        );

    if (previousPhase == Pv1bRequestPhaseLookaheadArmed) {
        InterlockedIncrement64(
            &Ctx->Counters.LookaheadSendInFlight
            );

        Pv1bLogEvent(
            "LOOKAHEAD-SEND-IN-FLIGHT",
            Ctx,
            RequestCtx->CycleId,
            PhysicalIndex,
            STATUS_PENDING
            );
    }

    sent = WdfRequestSend(
        Request,
        WdfDeviceGetIoTarget(Device),
        WDF_NO_SEND_OPTIONS
        );

    if (sent == FALSE) {
        sendStatus = WdfRequestGetStatus(
            Request
            );

        // Send failed: ownership never left this path, so RequestCtx is valid.
        // Restore the phase/counter only if this invocation promoted Armed->InFlight.
        if (previousPhase == Pv1bRequestPhaseLookaheadArmed) {
            (VOID)InterlockedCompareExchange(
                &RequestCtx->Phase,
                Pv1bRequestPhaseLookaheadArmed,
                Pv1bRequestPhaseLookaheadInFlight
                );

            InterlockedDecrement64(
                &Ctx->Counters.LookaheadSendInFlight
                );
        }

        Pv1bClearActiveLookaheadReference(
            Ctx,
            Request
            );

        InterlockedIncrement64(
            &Ctx->Counters.LookaheadSendFailures
            );

        Pv1bLogEvent(
            "LOOKAHEAD-SEND-SEND-FAIL",
            Ctx,
            RequestCtx->CycleId,
            PhysicalIndex,
            sendStatus
            );

        return sendStatus;
    }

    // Lifetime hard rule: no Request / RequestCtx dereference after TRUE.
    return STATUS_PENDING;
}

/*
 * PV0112: the release that closes what the reader has seen.
 *
 * The 0.1.10 lookahead transform: every contact of Visible keeps its slot,
 * ID and coordinates and loses its tip.  A contact that Newest names without
 * Confidence is cancelled (tip and Confidence cleared), because that is the
 * device saying "not a finger"; any other contact is completed.  The scan
 * time comes from Newest.  Nothing with a tip is ever produced.
 */
static VOID
Pv0112BuildRelease(
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Visible,
    _In_reads_bytes_(PV1B_REPORT_BYTES) const UCHAR* Newest,
    _Out_writes_bytes_(PV1B_REPORT_BYTES) UCHAR* Release,
    _Out_ PULONG CancelledCount
    )
{
    ULONG slot;
    ULONG newestSlot;
    ULONG cancelled;
    BOOLEAN newestNames;

    cancelled = 0u;

    RtlCopyMemory(
        Release,
        Visible,
        PV1B_REPORT_BYTES
        );

    // Same guard as the 0.1.10 lookahead release: a count of 0 is the
    // filter's zeroed report, which names no contact and carries no time.
    newestNames = (Newest[0] == PV1B_REPORT_ID_TOUCH &&
                   Newest[PV1B_CONTACT_COUNT_OFFSET] != 0u) ? TRUE : FALSE;

    for (slot = 0u; slot < PV1B_CONTACT_SLOT_COUNT; ++slot) {
        ULONG stateOffset;
        UCHAR clear;

        stateOffset =
            PV1B_CONTACT_SLOT_BASE_OFFSET +
            (slot * PV1B_CONTACT_SLOT_BYTES);

        if (Release[stateOffset] == PV1B_STATE_UNUSED_SENTINEL) {
            continue;
        }

        clear = (UCHAR)PV1B_STATE_TIP_BIT;

        for (newestSlot = 0u;
             newestNames && newestSlot < PV1B_CONTACT_SLOT_COUNT;
             ++newestSlot) {
            ULONG newestOffset;

            newestOffset =
                PV1B_CONTACT_SLOT_BASE_OFFSET +
                (newestSlot * PV1B_CONTACT_SLOT_BYTES);

            if (Newest[newestOffset] != PV1B_STATE_UNUSED_SENTINEL &&
                Newest[newestOffset + 1u] == Release[stateOffset + 1u]) {
                if ((Newest[newestOffset] & PV1B_STATE_CONFIDENCE_BIT) == 0u) {
                    clear = (UCHAR)(PV1B_STATE_TIP_BIT |
                                    PV1B_STATE_CONFIDENCE_BIT);
                    cancelled++;
                }
                break;
            }
        }

        Release[stateOffset] =
            (UCHAR)(Release[stateOffset] & ~clear);
    }

    if (newestNames) {
        Release[PV1B_SCAN_TIME_LOW_OFFSET] =
            Newest[PV1B_SCAN_TIME_LOW_OFFSET];
        Release[PV1B_SCAN_TIME_HIGH_OFFSET] =
            Newest[PV1B_SCAN_TIME_HIGH_OFFSET];
    }

    *CancelledCount = cancelled;
}

/*
 * PV0112: a read that came back with N >= 2 whole reports while the
 * pipeline holds something (HIDClass drained a backlog into one read).
 *
 * PV1K took exactly 64 bytes here and latched FatalBypass for the session.
 * 0.1.11 real-use log, 366.20 s: lookahead physicalIndex=3, info=640;
 * report 1 reached the reader, report 2 stayed in the FIFO for good, and
 * nothing was filtered for the remaining 18 minutes.
 *
 * A batch is a backlog, older than the stream it interrupts, and replaying
 * one is how a finished two-finger drag came back as a ghost zoom.  It also
 * cannot be taken in order: report 1, the lookahead tail and ten reports do
 * not fit the reader's ten-report buffer.  So the episode is closed instead:
 *   - report 1, the lookahead tail, the FIFO, a pending boundary release and
 *     the batch itself are dropped;
 *   - the reader gets one report that closes what it has already seen:
 *       the pending boundary release when one is waiting (it closes report 1
 *       of its own cycle, 0.1.10);
 *       else the last report it saw, every tip cleared (Pv0112BuildRelease,
 *       with the batch's newest report as the device's verdict);
 *       else, when it holds nothing, the zero touch report the future veto
 *       already sends.
 * Nothing with a tip is produced, so nothing is pressed and no press is left
 * without its release.  The next read opens a fresh episode through the
 * ordinary lookahead, future veto included.  The reader's cancel flag is
 * ignored on purpose, as the idle batch path does: a successful read handed
 * back as STATUS_CANCELLED stops the reader.
 *
 * Returns FALSE, leaving the PV1K path unchanged, when the completion is not
 * whole reports, when the bypass is already on, or when another request
 * owns the lookahead.
 */
static BOOLEAN
Pv0112TryCloseEpisodeOnBatch(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx,
    _In_ ULONG_PTR Information,
    _In_ ULONG HeldReports,
    _In_z_ PCSTR Where
    )
{
    NTSTATUS status;
    PVOID outputBuffer;
    size_t outputLength;
    UCHAR newest[PV1B_REPORT_BYTES];
    UCHAR visible[PV1B_REPORT_BYTES];
    UCHAR closeReport[PV1B_REPORT_BYTES];
    ULONG cancelled;
    LONG droppedDeferred;
    LONG droppedBoundary;
    LONG clearedEpisode;
    BOOLEAN owned;
    BOOLEAN haveVisible;
    BOOLEAN havePending;
    PCSTR closeKind;

    outputBuffer = NULL;
    outputLength = 0u;
    RtlZeroMemory(newest, sizeof(newest));
    RtlZeroMemory(visible, sizeof(visible));
    RtlZeroMemory(closeReport, sizeof(closeReport));
    cancelled = 0u;
    droppedDeferred = 0;
    droppedBoundary = 0;
    clearedEpisode = 0;
    owned = FALSE;
    haveVisible = FALSE;
    havePending = FALSE;
    closeKind = "NONE";

    if (Information <= PV1B_REPORT_BYTES ||
        (Information % PV1B_REPORT_BYTES) != 0u) {
        return FALSE;
    }

    status = Pv1bRetrieveOutput(
        Request,
        &outputBuffer,
        &outputLength
        );

    if (!NT_SUCCESS(status) ||
        outputBuffer == NULL ||
        outputLength < Information) {
        return FALSE;
    }

    RtlCopyMemory(
        newest,
        (const UCHAR*)outputBuffer + (Information - PV1B_REPORT_BYTES),
        PV1B_REPORT_BYTES
        );

    WdfSpinLockAcquire(Ctx->StateLock);

    if (Ctx->FatalBypass == 0 &&
        Ctx->ActiveLookaheadRequest == WDF_NO_HANDLE) {
        owned = TRUE;

        if (Ctx->BoundaryReleasePending != 0 &&
            Ctx->DeferredValid == 1 &&
            Ctx->DeferredHead < PV1B_DEFERRED_SLOTS &&
            !Pv1bIsActiveTouchReport(
                Ctx->DeferredReport[Ctx->DeferredHead]
                )) {
            RtlCopyMemory(
                closeReport,
                Ctx->DeferredReport[Ctx->DeferredHead],
                PV1B_REPORT_BYTES
                );
            havePending = TRUE;
        }

        if (Ctx->LastUpperTouchValid != 0) {
            RtlCopyMemory(
                visible,
                Ctx->LastUpperTouchReport,
                PV1B_REPORT_BYTES
                );
            haveVisible = TRUE;
        }

        droppedDeferred = InterlockedExchange(&Ctx->DeferredValid, 0);
        droppedBoundary = InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
        Ctx->DeferredHead = 0u;
        clearedEpisode = InterlockedExchange(
            &Ctx->PhysicalEpisodeActive,
            0
            );
        InterlockedExchange(&Ctx->FutureVetoActive, 0);
        InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
    }

    WdfSpinLockRelease(Ctx->StateLock);

    if (!owned) {
        return FALSE;
    }

    InterlockedExchange64(&Ctx->LastRollingOutputQpc, 0);

    if (clearedEpisode != 0) {
        InterlockedIncrement64(&Ctx->Counters.EpisodeEnds);
    }

    if (havePending) {
        closeKind = "PENDING-BOUNDARY-RELEASE";
    }
    else if (haveVisible &&
             Pv1bIsActiveTouchReport(visible)) {
        Pv0112BuildRelease(
            visible,
            newest,
            closeReport,
            &cancelled
            );
        closeKind = "RELEASE-LAST-VISIBLE";
    }
    else {
        Pv1eBuildZeroTouchReport(closeReport);
        closeKind = "ZERO-NOTHING-VISIBLE";
    }

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_ERROR_LEVEL,
        "PalmRejPipeline PV0112: BATCH-CLOSE where=%s cycle=%I64d dev=%ld info=%Iu reports=%Iu newestCount=%u newestActive=%ld kind=%s cancel=%lu heldReports=%lu droppedDeferred=%ld droppedBoundaryRelease=%ld episodeClosed=%ld out=%02X,%02X,%02X,%02X,%02X,%02X,%02X,%02X,%02X,%02X/%u total=%I64d\n",
        Where,
        RequestCtx->CycleId,
        Ctx->DeviceId,
        Information,
        Information / PV1B_REPORT_BYTES,
        (ULONG)newest[PV1B_CONTACT_COUNT_OFFSET],
        Pv1bIsActiveTouchReport(newest) ? 1L : 0L,
        closeKind,
        cancelled,
        HeldReports,
        droppedDeferred,
        droppedBoundary,
        clearedEpisode,
        (ULONG)closeReport[1], (ULONG)closeReport[7], (ULONG)closeReport[13],
        (ULONG)closeReport[19], (ULONG)closeReport[25], (ULONG)closeReport[31],
        (ULONG)closeReport[37], (ULONG)closeReport[43], (ULONG)closeReport[49],
        (ULONG)closeReport[55],
        (ULONG)closeReport[PV1B_CONTACT_COUNT_OFFSET],
        InterlockedIncrement64(&g_V0112BatchCloses)
        );

    if (!Pv1bCopyReportToRequest(
            Request,
            closeReport
            )) {
        // Cannot happen: the batch itself filled at least 128 bytes of it.
        Pv0112EnterBypass(
            Ctx,
            "BATCH-CLOSE-COPY-FAIL",
            RequestCtx->CycleId,
            FALSE
            );

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_DATA_ERROR,
            0u,
            "PV0112-BATCH-CLOSE-COPY-FAIL"
            );

        return TRUE;
    }

    Pv1bCompleteUpper(
        Request,
        RequestCtx,
        Ctx,
        STATUS_SUCCESS,
        PV1B_REPORT_BYTES,
        "PV0112-BATCH-CLOSE-COMPLETE"
        );

    return TRUE;
}

VOID
EvtPv1bFirstCompletion(
    _In_ WDFREQUEST Request,
    _In_ WDFIOTARGET Target,
    _In_ PWDF_REQUEST_COMPLETION_PARAMS Params,
    _In_ WDFCONTEXT Context
    )
{
    PPV1B_REQUEST_CONTEXT requestCtx;
    WDFDEVICE device;
    PDEVICE_CONTEXT ctx;
    NTSTATUS status;
    ULONG_PTR information;
    BOOLEAN copied;
    BOOLEAN canceled;
    NTSTATUS lookaheadStatus;

    UNREFERENCED_PARAMETER(Target);

    requestCtx = (PPV1B_REQUEST_CONTEXT)Context;
    device = requestCtx->Device;
    ctx = DeviceGetContext(
        device
        );

    status = Params->IoStatus.Status;
    information = Params->IoStatus.Information;

    InterlockedIncrement64(
        &ctx->Counters.FirstCompletions
        );

    if (!NT_SUCCESS(status)) {
        InterlockedIncrement64(
            &ctx->Counters.FirstCompletionFailures
            );

        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            status,
            information,
            "FIRST-COMPLETE-FAIL-UPPER"
            );

        return;
    }

    if (information != PV1B_REPORT_BYTES) {
        Pv1fLogCompletionShape(
            "FIRST-COMPLETION-NON64",
            ctx,
            Request,
            requestCtx->CycleId,
            information
            );

        if (Pv1fIsSupportedIdleBatchCompletion(
                ctx,
                Request,
                information
                )) {
            DbgPrintEx(
                DPFLTR_IHVDRIVER_ID,
                DPFLTR_ERROR_LEVEL,
                "PalmRejPipeline PV0112: BATCH-IDLE-DIRECT cycle=%I64d dev=%ld info=%Iu reports=%Iu action=PASS-UNCHANGED\n",
                requestCtx->CycleId,
                ctx->DeviceId,
                information,
                information / PV1B_REPORT_BYTES
                );

            Pv1bCompleteUpper(
                Request,
                requestCtx,
                ctx,
                status,
                information,
                "IDLE-BATCH-DIRECT-ONE-SHOT"
                );

            return;
        }

        // PV0112: a batch while the pipeline holds something closes the
        // episode instead of latching the bypass.
        if (Pv0112TryCloseEpisodeOnBatch(
                ctx,
                Request,
                requestCtx,
                information,
                0u,
                "FIRST"
                )) {
            return;
        }
    }

    copied = Pv1bCopyReportFromRequest(
        Request,
        information,
        requestCtx->FirstReport
        );

    if (!copied) {
        Pv0112EnterBypass(
            ctx,
            "FIRST-BUFFER-UNAVAILABLE",
            requestCtx->CycleId,
            FALSE
            );

        InterlockedIncrement64(
            &ctx->Counters.FatalBypassEvents
            );

        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            status,
            information,
            "FIRST-BUFFER-UNAVAILABLE-BYPASS"
            );

        return;
    }

    Pv1bLogReportMeta(
        "FIRST-REPORT-META",
        ctx,
        requestCtx->CycleId,
        requestCtx->FirstReport,
        information
        );

    // PV1E steady state (PV1D FIFO semantics preserved): a new physical completion clocks exactly one
    // chronological active FIFO output. EvtIoDefault may complete only the
    // separately marked real non-active boundary release.
    if (Pv1dTryRollingExchange(
            ctx,
            Request,
            requestCtx,
            requestCtx->FirstReport,
            information
            )) {
        return;
    }

    if (!Pv1bIsActiveTouchReport(
            requestCtx->FirstReport
            )) {
        if (InterlockedCompareExchange(
                &ctx->PhysicalEpisodeActive,
                0,
                0
                ) != 0) {
            Pv1bMarkEpisodeEnd(
                ctx,
                requestCtx->CycleId,
                0,
                "EPISODE-END-FIRST-NONACTIVE-DIRECT"
                );

            InterlockedIncrement64(
                &ctx->Counters.TailDrainCompletions
                );

            Pv1bLogEvent(
                "EPISODE-TAIL-DRAIN-COMPLETE",
                ctx,
                requestCtx->CycleId,
                0,
                0
                );
        }

        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            status,
            information,
            "FIRST-NONACTIVE-UPPER"
            );

        return;
    }

    InterlockedIncrement64(
        &ctx->Counters.FirstActiveReports
        );

    if (!Pv1bEnsureEpisodeStartInvariant(
            ctx,
            requestCtx->CycleId
            )) {
        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            STATUS_DEVICE_PROTOCOL_ERROR,
            0u,
            "EPISODE-START-INVARIANT-FAIL-UPPER"
            );

        return;
    }

    Pv1eObserveFutureVetoCandidate(
        ctx,
        requestCtx,
        requestCtx->FirstReport,
        1u
        );

    canceled = WdfRequestIsCanceled(
        Request
        );

    if (canceled) {
        InterlockedIncrement64(
            &ctx->Counters.FirstCanceledBeforeReuse
            );

        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            STATUS_CANCELLED,
            0u,
            "FIRST-CANCELED-BEFORE-REUSE"
            );

        return;
    }

    requestCtx->FirstInformation = information;

    lookaheadStatus = Pv1bSendLookahead(
        device,
        ctx,
        Request,
        requestCtx,
        2u
        );

    if (lookaheadStatus == STATUS_PENDING) {
        return;
    }

    if (lookaheadStatus == STATUS_CANCELLED) {
        InterlockedIncrement64(
            &ctx->Counters.FirstCanceledBeforeReuse
            );

        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            STATUS_CANCELLED,
            0u,
            "FIRST-CANCELED-BEFORE-LOOKAHEAD"
            );

        return;
    }

    InterlockedIncrement64(
        &ctx->Counters.FatalBypassEvents
        );

    Pv0112EnterBypass(
        ctx,
        "LOOKAHEAD-SETUP-FAIL",
        requestCtx->CycleId,
        FALSE
        );

    Pv1bRestoreFirstAndComplete(
        Request,
        requestCtx,
        ctx,
        "LOOKAHEAD-SETUP-FAIL-FIRST-RESTORED"
        );
}

VOID
EvtPv1bLookaheadCompletion(
    _In_ WDFREQUEST Request,
    _In_ WDFIOTARGET Target,
    _In_ PWDF_REQUEST_COMPLETION_PARAMS Params,
    _In_ WDFCONTEXT Context
    )
{
    PPV1B_REQUEST_CONTEXT requestCtx;
    WDFDEVICE device;
    PDEVICE_CONTEXT ctx;
    NTSTATUS status;
    ULONG_PTR information;
    ULONG physicalIndex;
    ULONG slotIndex;
    BOOLEAN copied;
    BOOLEAN canceled;
    LONG previousPhase;
    NTSTATUS nextStatus;
    LONG tailCount;

    UNREFERENCED_PARAMETER(Target);

    requestCtx = (PPV1B_REQUEST_CONTEXT)Context;
    device = requestCtx->Device;
    ctx = DeviceGetContext(
        device
        );

    status = Params->IoStatus.Status;
    information = Params->IoStatus.Information;
    physicalIndex = requestCtx->CurrentPhysicalIndex;

    previousPhase = InterlockedExchange(
        &requestCtx->Phase,
        Pv1bRequestPhaseLookaheadComplete
        );

    Pv1bClearActiveLookaheadReference(
        ctx,
        Request
        );

    InterlockedIncrement64(
        &ctx->Counters.LookaheadCompletions
        );

    Pv1bLogLookaheadRequestContext(
        "LOOKAHEAD-SEND-COMPLETE",
        ctx,
        Request,
        requestCtx->CycleId,
        physicalIndex,
        previousPhase,
        status
        );

    if (physicalIndex < 2u ||
        physicalIndex > PV1B_TARGET_REPORT_DEPTH) {
        InterlockedIncrement64(
            &ctx->Counters.FatalBypassEvents
            );

        // PV0112 sticky: Pv1bSendLookahead only sends 2..8, so this request
        // context is not what the pipeline wrote.  Keep the PV1K latch.
        Pv0112EnterBypass(
            ctx,
            "LOOKAHEAD-PHYSICAL-INDEX",
            requestCtx->CycleId,
            TRUE
            );

        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            STATUS_DEVICE_PROTOCOL_ERROR,
            0u,
            "LOOKAHEAD-PHYSICAL-INDEX-HARD-FAIL"
            );

        return;
    }

    canceled = WdfRequestIsCanceled(
        Request
        );

    if (canceled || !NT_SUCCESS(status)) {
        InterlockedIncrement64(
            &ctx->Counters.LookaheadCompletionFailures
            );

        if (canceled || status == STATUS_CANCELLED) {
            InterlockedIncrement64(
                &ctx->Counters.LookaheadCompletionCanceled
                );

            Pv1bCompleteUpper(
                Request,
                requestCtx,
                ctx,
                STATUS_CANCELLED,
                0u,
                "LOOKAHEAD-CANCELED-UPPER"
                );

            return;
        }

        InterlockedIncrement64(
            &ctx->Counters.FatalBypassEvents
            );

        Pv0112EnterBypass(
            ctx,
            "LOOKAHEAD-COMPLETION-FAIL",
            requestCtx->CycleId,
            FALSE
            );

        if (requestCtx->StoredLookaheadCount > 0u) {
            if (Pv1bStoreDeferredTail(
                    ctx,
                    requestCtx,
                    (LONG)requestCtx->StoredLookaheadCount,
                    requestCtx->CycleId
                    )) {
                Pv1bRestoreFirstAndComplete(
                    Request,
                    requestCtx,
                    ctx,
                    "LOOKAHEAD-FAIL-FALLBACK-PREVIOUS-TAIL"
                    );

                return;
            }
        }

        Pv1bRestoreFirstAndComplete(
            Request,
            requestCtx,
            ctx,
            "LOOKAHEAD-FAIL-FIRST-RESTORED"
            );

        return;
    }

    slotIndex = physicalIndex - 2u;

    if (slotIndex >= PV1B_DEFERRED_SLOTS) {
        InterlockedIncrement64(
            &ctx->Counters.FatalBypassEvents
            );

        // PV0112 sticky: same broken request context as above.
        Pv0112EnterBypass(
            ctx,
            "LOOKAHEAD-SLOT-INDEX",
            requestCtx->CycleId,
            TRUE
            );

        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            STATUS_DEVICE_PROTOCOL_ERROR,
            0u,
            "LOOKAHEAD-SLOT-INDEX-HARD-FAIL"
            );

        return;
    }

    if (information != PV1B_REPORT_BYTES) {
        Pv1fLogCompletionShape(
            "LOOKAHEAD-COMPLETION-NON64",
            ctx,
            Request,
            requestCtx->CycleId,
            information
            );

        // PV0112: the 366.20 s latch.  A batch in the lookahead closes the
        // episode instead of latching the bypass; report 1 and the stored
        // tail are dropped with it.
        if (Pv0112TryCloseEpisodeOnBatch(
                ctx,
                Request,
                requestCtx,
                information,
                1u + requestCtx->StoredLookaheadCount,
                "LOOKAHEAD"
                )) {
            return;
        }
    }

    copied = Pv1bCopyReportFromRequest(
        Request,
        information,
        requestCtx->LookaheadReport[slotIndex]
        );

    if (!copied) {
        InterlockedIncrement64(
            &ctx->Counters.LookaheadCompletionFailures
            );

        InterlockedIncrement64(
            &ctx->Counters.FatalBypassEvents
            );

        Pv0112EnterBypass(
            ctx,
            "LOOKAHEAD-BUFFER-FAIL",
            requestCtx->CycleId,
            FALSE
            );

        if (requestCtx->StoredLookaheadCount > 0u) {
            if (Pv1bStoreDeferredTail(
                    ctx,
                    requestCtx,
                    (LONG)requestCtx->StoredLookaheadCount,
                    requestCtx->CycleId
                    )) {
                Pv1bRestoreFirstAndComplete(
                    Request,
                    requestCtx,
                    ctx,
                    "LOOKAHEAD-BUFFER-FAIL-FALLBACK-PREVIOUS-TAIL"
                    );

                return;
            }
        }

        Pv1bRestoreFirstAndComplete(
            Request,
            requestCtx,
            ctx,
            "LOOKAHEAD-BUFFER-FAIL-FIRST-RESTORED"
            );

        return;
    }

    requestCtx->LookaheadInformation[slotIndex] =
        information;
    requestCtx->StoredLookaheadCount =
        physicalIndex - 1u;

    InterlockedIncrement64(
        &ctx->Counters.LookaheadCompletionSuccesses
        );

    Pv1bLogLookaheadReportMeta(
        ctx,
        requestCtx->CycleId,
        physicalIndex,
        requestCtx->LookaheadReport[slotIndex],
        information
        );

    Pv1eObserveFutureVetoCandidate(
        ctx,
        requestCtx,
        requestCtx->LookaheadReport[slotIndex],
        physicalIndex
        );

    if (!Pv1bIsActiveTouchReport(
            requestCtx->LookaheadReport[slotIndex]
            )) {
        const UCHAR* v0110Release;
        UCHAR v0110Coherent[PV1B_REPORT_BYTES];

        tailCount = (LONG)requestCtx->StoredLookaheadCount;

        Pv1bMarkEpisodeEnd(
            ctx,
            requestCtx->CycleId,
            tailCount,
            "EPISODE-END-AT-LOOKAHEAD-NONACTIVE"
            );

        /*
         * 0.1.10: the release has to close what the reader actually saw.
         *
         * On this path the reader sees report 1 and then this release; every
         * report in between is dropped as the active tail.  When a contact
         * lifted inside that tail, the physical release no longer names it,
         * and the reader is left holding a contact that never goes up.
         * Measured 2026-09-21 ("0.1.9 두 손가락 탭.log"): 10 of 29 two-finger
         * taps went "two down, one up" this way and 13 of 34 on 0.1.8; the
         * repair changes 32 of 138 lookahead releases in the 0.1.x logs.  The
         * rolling path has had
         * this repair since PV1J; this one never did.
         *
         * If any contact report 1 had down is missing from the release, the
         * release is rebuilt from report 1 with every tip cleared - the same
         * transform PV1J makes - keeping report 1's own count and taking the
         * physical release's scan time.  Nothing with a tip is ever added,
         * so this cannot press anything.  A report 1 the future veto is about
         * to zero is left alone: the reader never sees it.
         */
        v0110Release = requestCtx->LookaheadReport[slotIndex];

        if (InterlockedCompareExchange(
                &requestCtx->FutureVetoCandidate,
                0,
                0
                ) == 0 &&
            requestCtx->FirstReport[0] == PV1B_REPORT_ID_TOUCH &&
            v0110Release[0] == PV1B_REPORT_ID_TOUCH &&
            /*
             * A count of 0 is the filter's zeroed report (a suppressed
             * frame), not the device lifting anything.  Treating it as a
             * lift would close a touch the filter may pass again a few
             * frames later - a click before the stroke.
             */
            v0110Release[PV1B_CONTACT_COUNT_OFFSET] != 0u) {
            ULONG v0110fs;
            BOOLEAN v0110Missing = FALSE;

            for (v0110fs = 0u;
                 v0110fs < PV1B_CONTACT_SLOT_COUNT && !v0110Missing;
                 ++v0110fs) {
                ULONG v0110fo;
                UCHAR v0110fstate;
                UCHAR v0110fid;
                ULONG v0110rs;
                BOOLEAN v0110Named = FALSE;

                v0110fo = PV1B_CONTACT_SLOT_BASE_OFFSET +
                          (v0110fs * PV1B_CONTACT_SLOT_BYTES);
                v0110fstate = requestCtx->FirstReport[v0110fo];

                if (v0110fstate == PV1B_STATE_UNUSED_SENTINEL ||
                    (v0110fstate & PV1B_STATE_TIP_BIT) == 0u) {
                    continue;
                }

                v0110fid = requestCtx->FirstReport[v0110fo + 1u];

                for (v0110rs = 0u; v0110rs < PV1B_CONTACT_SLOT_COUNT; ++v0110rs) {
                    ULONG v0110ro =
                        PV1B_CONTACT_SLOT_BASE_OFFSET +
                        (v0110rs * PV1B_CONTACT_SLOT_BYTES);

                    if (v0110Release[v0110ro] != PV1B_STATE_UNUSED_SENTINEL &&
                        v0110Release[v0110ro + 1u] == v0110fid) {
                        v0110Named = TRUE;
                        break;
                    }
                }

                if (!v0110Named) {
                    v0110Missing = TRUE;
                }
            }

            if (v0110Missing) {
                ULONG v0110slot;
                ULONG v0110Cancelled = 0u;

                RtlCopyMemory(
                    v0110Coherent,
                    requestCtx->FirstReport,
                    PV1B_REPORT_BYTES
                    );

                for (v0110slot = 0u;
                     v0110slot < PV1B_CONTACT_SLOT_COUNT;
                     ++v0110slot) {
                    ULONG v0110so =
                        PV1B_CONTACT_SLOT_BASE_OFFSET +
                        (v0110slot * PV1B_CONTACT_SLOT_BYTES);

                    if (v0110Coherent[v0110so] != PV1B_STATE_UNUSED_SENTINEL) {
                        UCHAR v0110Clear = (UCHAR)PV1B_STATE_TIP_BIT;
                        ULONG v0110rc;

                        /*
                         * The device ends a contact it has decided is not a
                         * finger with Confidence cleared as well (state 00):
                         * the host is to cancel it, not complete it.  That is
                         * a per-contact decision - mixed releases (02 next to
                         * 00) are common - so a contact the release names keeps
                         * the release's verdict, and one it does not name is
                         * completed, as PV1J does.
                         */
                        for (v0110rc = 0u; v0110rc < PV1B_CONTACT_SLOT_COUNT; ++v0110rc) {
                            ULONG v0110ro =
                                PV1B_CONTACT_SLOT_BASE_OFFSET +
                                (v0110rc * PV1B_CONTACT_SLOT_BYTES);

                            if (v0110Release[v0110ro] != PV1B_STATE_UNUSED_SENTINEL &&
                                v0110Release[v0110ro + 1u] == v0110Coherent[v0110so + 1u]) {
                                if ((v0110Release[v0110ro] & PV1B_STATE_CONFIDENCE_BIT) == 0u) {
                                    v0110Clear = (UCHAR)(PV1B_STATE_TIP_BIT |
                                                         PV1B_STATE_CONFIDENCE_BIT);
                                    v0110Cancelled++;
                                }
                                break;
                            }
                        }

                        v0110Coherent[v0110so] =
                            (UCHAR)(v0110Coherent[v0110so] & ~v0110Clear);
                    }
                }

                v0110Coherent[PV1B_SCAN_TIME_LOW_OFFSET] =
                    v0110Release[PV1B_SCAN_TIME_LOW_OFFSET];
                v0110Coherent[PV1B_SCAN_TIME_HIGH_OFFSET] =
                    v0110Release[PV1B_SCAN_TIME_HIGH_OFFSET];

                DbgPrint(
                    "PalmRejPipeline PV0110: LOOKAHEAD-COHERENT-RELEASE cycle=%I64d dev=%ld physicalIndex=%lu firstCount=%u releaseCount=%u cancel=%u total=%I64d\n",
                    requestCtx->CycleId,
                    ctx->DeviceId,
                    physicalIndex,
                    (unsigned int)requestCtx->FirstReport[PV1B_CONTACT_COUNT_OFFSET],
                    (unsigned int)v0110Release[PV1B_CONTACT_COUNT_OFFSET],
                    (unsigned int)v0110Cancelled,
                    InterlockedIncrement64(&g_V0110CoherentReleases)
                    );

                v0110Release = v0110Coherent;
            }
        }

        // Preserve only the real release. Reports between report 1 and this
        // boundary are the active tail of the now-closed episode and must
        // never be replayed when a later touch supplies the next clock.
        if (!Pv1dStoreBoundaryRelease(
                ctx,
                v0110Release,
                information,
                tailCount - 1,
                requestCtx->CycleId
                )) {
            Pv1bRestoreFirstAndComplete(
                Request,
                requestCtx,
                ctx,
                "REPORT1-RESTORED-A-AFTER-BOUNDARY-STORE-FAIL"
                );

            return;
        }

        if (InterlockedCompareExchange(
                &requestCtx->FutureVetoCandidate,
                0,
                0
                ) != 0) {
            InterlockedExchange(
                &requestCtx->FutureVetoSuppressFirst,
                1
                );
            InterlockedIncrement64(
                &ctx->Counters.FutureVetoBoundaryCommits
                );

            DbgPrint(
                "PalmRejPipeline PV1I: FUTURE-VETO-COMMIT-BOUNDARY cycle=%I64d dev=%ld physicalIndex=%lu reason=0x%02lX firstPhysicalIndex=%lu\n",
                requestCtx->CycleId,
                ctx->DeviceId,
                physicalIndex,
                requestCtx->FutureVetoReasonMask,
                requestCtx->FutureVetoFirstPhysicalIndex
                );
        }

        Pv1bRestoreFirstAndComplete(
            Request,
            requestCtx,
            ctx,
            "REPORT1-RESTORED-A-COMPLETE-BOUNDARY-RELEASE-PENDING"
            );

        return;
    }

    if (physicalIndex == PV1B_TARGET_REPORT_DEPTH) {
        tailCount = (LONG)requestCtx->StoredLookaheadCount;

        if (!Pv1bStoreDeferredTail(
                ctx,
                requestCtx,
                tailCount,
                requestCtx->CycleId
                )) {
            Pv1bRestoreFirstAndComplete(
                Request,
                requestCtx,
                ctx,
                "REPORT1-RESTORED-A-AFTER-FULL-DEPTH-STORE-FAIL"
                );

            return;
        }

        if (InterlockedCompareExchange(
                &requestCtx->FutureVetoCandidate,
                0,
                0
                ) != 0) {
            WdfSpinLockAcquire(ctx->StateLock);
            InterlockedExchange(&ctx->FutureVetoActive, 1);
            InterlockedExchange(
                &ctx->FutureVetoReasonMask,
                (LONG)requestCtx->FutureVetoReasonMask
                );
            WdfSpinLockRelease(ctx->StateLock);

            InterlockedExchange(
                &requestCtx->FutureVetoSuppressFirst,
                1
                );
            InterlockedIncrement64(
                &ctx->Counters.FutureVetoFullDepthCommits
                );

            DbgPrint(
                "PalmRejPipeline PV1I: FUTURE-VETO-COMMIT-FULL-DEPTH cycle=%I64d dev=%ld reason=0x%02lX firstPhysicalIndex=%lu deferredCount=%ld\n",
                requestCtx->CycleId,
                ctx->DeviceId,
                requestCtx->FutureVetoReasonMask,
                requestCtx->FutureVetoFirstPhysicalIndex,
                tailCount
                );
        }

        Pv1bRestoreFirstAndComplete(
            Request,
            requestCtx,
            ctx,
            "REPORT1-RESTORED-A-COMPLETE-FULL-DEPTH8"
            );

        return;
    }

    nextStatus = Pv1bSendLookahead(
        device,
        ctx,
        Request,
        requestCtx,
        physicalIndex + 1u
        );

    if (nextStatus == STATUS_PENDING) {
        return;
    }

    if (nextStatus == STATUS_CANCELLED) {
        Pv1bCompleteUpper(
            Request,
            requestCtx,
            ctx,
            STATUS_CANCELLED,
            0u,
            "LOOKAHEAD-CANCELED-BEFORE-NEXT-REUSE"
            );

        return;
    }

    InterlockedIncrement64(
        &ctx->Counters.FatalBypassEvents
        );

    Pv0112EnterBypass(
        ctx,
        "LOOKAHEAD-NEXT-SETUP-FAIL",
        requestCtx->CycleId,
        FALSE
        );

    if (Pv1bStoreDeferredTail(
            ctx,
            requestCtx,
            (LONG)requestCtx->StoredLookaheadCount,
            requestCtx->CycleId
            )) {
        Pv1bRestoreFirstAndComplete(
            Request,
            requestCtx,
            ctx,
            "LOOKAHEAD-NEXT-SETUP-FAIL-FALLBACK"
            );

        return;
    }

    Pv1bRestoreFirstAndComplete(
        Request,
        requestCtx,
        ctx,
        "LOOKAHEAD-NEXT-SETUP-FAIL-FIRST-RESTORED"
        );
}


static PCSTR
Pv1bDeferredCompletionEvent(
    _In_ ULONG SlotIndex
    )
{
    switch (SlotIndex) {
    case 0u:
        return "REPORT2-DEFERRED-B-COMPLETE";
    case 1u:
        return "REPORT3-DEFERRED-C-COMPLETE";
    case 2u:
        return "REPORT4-DEFERRED-D-COMPLETE";
    case 3u:
        return "REPORT5-DEFERRED-E-COMPLETE";
    case 4u:
        return "REPORT6-DEFERRED-F-COMPLETE";
    case 5u:
        return "REPORT7-DEFERRED-G-COMPLETE";
    case 6u:
        return "REPORT8-DEFERRED-H-COMPLETE";
    default:
        return "DEFERRED-UNKNOWN-COMPLETE";
    }
}

static BOOLEAN
Pv1dTryDeliverBoundaryRelease(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx
    )
{
    UCHAR releaseReport[PV1B_REPORT_BYTES];
    ULONG_PTR releaseInformation;
    LONG pending;
    LONG count;
    ULONG head;
    BOOLEAN valid;
    BOOLEAN copied;
    BOOLEAN canceled;

    RtlZeroMemory(releaseReport, sizeof(releaseReport));
    releaseInformation = 0u;
    pending = 0;
    count = 0;
    head = 0u;
    valid = FALSE;
    canceled = FALSE;

    WdfSpinLockAcquire(Ctx->StateLock);

    pending = Ctx->BoundaryReleasePending;

    if (pending != 0) {
        canceled = WdfRequestIsCanceled(Request);
        count = Ctx->DeferredValid;
        head = Ctx->DeferredHead;

        if (count == 1 &&
            head < PV1B_DEFERRED_SLOTS &&
            !Pv1bIsActiveTouchReport(Ctx->DeferredReport[head])) {
            RtlCopyMemory(
                releaseReport,
                Ctx->DeferredReport[head],
                PV1B_REPORT_BYTES
                );
            releaseInformation = Ctx->DeferredInformation[head];
            valid = TRUE;
        }

        if (!canceled) {
            InterlockedExchange(&Ctx->DeferredValid, 0);
            InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
            Ctx->DeferredHead = 0u;
        }
    }

    WdfSpinLockRelease(Ctx->StateLock);

    if (pending == 0) {
        return FALSE;
    }

    RequestCtx->Phase = Pv1bRequestPhaseDeferredDelivery;

    if (canceled) {
        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_CANCELLED,
            0u,
            "BOUNDARY-RELEASE-UPPER-CANCELED-PRESERVED"
            );

        return TRUE;
    }

    if (!valid) {
        InterlockedIncrement64(
            &Ctx->Counters.BoundaryReleaseInvariantFailures
            );
        InterlockedIncrement64(&Ctx->Counters.FatalBypassEvents);
        Pv0112EnterBypass(Ctx, "BOUNDARY-RELEASE-INVARIANT-FAIL", RequestCtx->CycleId, FALSE);

        Pv1bLogEvent(
            "BOUNDARY-RELEASE-INVARIANT-FAIL",
            Ctx,
            RequestCtx->CycleId,
            count,
            head
            );

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_DEVICE_PROTOCOL_ERROR,
            0u,
            "BOUNDARY-RELEASE-INVARIANT-FAIL-UPPER"
            );

        return TRUE;
    }

    copied = Pv1bCopyReportToRequest(Request, releaseReport);

    if (!copied) {
        InterlockedIncrement64(
            &Ctx->Counters.BoundaryReleaseInvariantFailures
            );
        InterlockedIncrement64(&Ctx->Counters.FatalBypassEvents);
        Pv0112EnterBypass(Ctx, "BOUNDARY-RELEASE-COPY-FAIL", RequestCtx->CycleId, FALSE);

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_DATA_ERROR,
            0u,
            "BOUNDARY-RELEASE-COPY-FAIL-UPPER"
            );

        return TRUE;
    }

    InterlockedIncrement64(&Ctx->Counters.DeferredDeliveries);
    InterlockedIncrement64(&Ctx->Counters.BoundaryReleaseDeliveries);
    InterlockedIncrement64(&Ctx->Counters.TailDrainCompletions);

    Pv1bLogReportMeta(
        "BOUNDARY-RELEASE-DEFERRED-META",
        Ctx,
        RequestCtx->CycleId,
        releaseReport,
        releaseInformation
        );

    Pv1bLogEvent(
        "BOUNDARY-RELEASE-DEFERRED-COMPLETE",
        Ctx,
        RequestCtx->CycleId,
        count,
        0
        );

    Pv1bCompleteUpper(
        Request,
        RequestCtx,
        Ctx,
        STATUS_SUCCESS,
        releaseInformation,
        "BOUNDARY-RELEASE-UPPER-COMPLETE"
        );

    return TRUE;
}

BOOLEAN
Pv1bTryDeliverDeferred(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx
    )
{
    UCHAR report[PV1B_REPORT_BYTES];
    ULONG_PTR information;
    BOOLEAN haveDeferred;
    BOOLEAN copied;
    ULONG slotIndex;
    LONG countBefore;
    LONG countAfter;
    BOOLEAN reportActive;
    PCSTR completionEvent;
    ULONG physicalIndex;

    RtlZeroMemory(
        report,
        sizeof(report)
        );

    information = 0u;
    haveDeferred = FALSE;
    slotIndex = 0u;
    countBefore = 0;
    countAfter = 0;
    completionEvent = "DEFERRED-UNKNOWN-COMPLETE";
    physicalIndex = 0u;

    // If this upper request is already canceled, leave FIFO untouched.
    if (WdfRequestIsCanceled(Request)) {
        RequestCtx->Phase =
            Pv1bRequestPhaseDeferredDelivery;

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_CANCELLED,
            0u,
            "DEFERRED-UPPER-CANCELED-BEFORE-POP"
            );

        return TRUE;
    }

    WdfSpinLockAcquire(
        Ctx->StateLock
        );

    countBefore = Ctx->DeferredValid;

    if (countBefore > 0 &&
        countBefore <= (LONG)PV1B_DEFERRED_SLOTS) {
        slotIndex = Ctx->DeferredHead;

        if (slotIndex >= PV1B_DEFERRED_SLOTS) {
            slotIndex = 0u;
            InterlockedIncrement64(
                &Ctx->Counters.FatalBypassEvents
                );
            Pv0112EnterBypass(
                Ctx,
                "DEFERRED-HEAD-OUT-OF-RANGE",
                RequestCtx->CycleId,
                FALSE
                );
        }

        RtlCopyMemory(
            report,
            Ctx->DeferredReport[slotIndex],
            PV1B_REPORT_BYTES
            );

        information =
            Ctx->DeferredInformation[slotIndex];

        countAfter = countBefore - 1;

        if (countAfter > 0) {
            Ctx->DeferredHead =
                (slotIndex + 1u) % PV1B_DEFERRED_SLOTS;
        }
        else {
            Ctx->DeferredHead = 0u;
        }

        InterlockedExchange(
            &Ctx->DeferredValid,
            countAfter
            );

        haveDeferred = TRUE;
    }
    else if (countBefore < 0 ||
             countBefore > (LONG)PV1B_DEFERRED_SLOTS) {
        InterlockedIncrement64(
            &Ctx->Counters.FatalBypassEvents
            );

        Pv0112EnterBypass(
            Ctx,
            "DEFERRED-COUNT-OUT-OF-RANGE",
            RequestCtx->CycleId,
            FALSE
            );

        InterlockedExchange(
            &Ctx->DeferredValid,
            0
            );

        InterlockedExchange(
            &Ctx->BoundaryReleasePending,
            0
            );

        Ctx->DeferredHead = 0u;
    }

    WdfSpinLockRelease(
        Ctx->StateLock
        );

    if (!haveDeferred) {
        return FALSE;
    }

    RequestCtx->Phase =
        Pv1bRequestPhaseDeferredDelivery;

    Pv1bLogReportMeta(
        "DEFERRED-DELIVERY-META",
        Ctx,
        RequestCtx->CycleId,
        report,
        information
        );

    reportActive = Pv1bIsActiveTouchReport(
        report
        );

    if (!reportActive) {
        if (countAfter != 0) {
            InterlockedIncrement64(
                &Ctx->Counters.EpisodeBoundaryStaleFifoFailures
                );

            InterlockedIncrement64(
                &Ctx->Counters.FatalBypassEvents
                );

            Pv0112EnterBypass(
                Ctx,
                "EPISODE-BOUNDARY-STALE-FIFO",
                RequestCtx->CycleId,
                FALSE
                );

            Pv1bLogEvent(
                "EPISODE-BOUNDARY-STALE-FIFO-HARD-FAIL",
                Ctx,
                RequestCtx->CycleId,
                countBefore,
                countAfter
                );
        }
        else {
            InterlockedIncrement64(
                &Ctx->Counters.TailDrainCompletions
                );

            Pv1bLogEvent(
                "EPISODE-TAIL-DRAIN-COMPLETE",
                Ctx,
                RequestCtx->CycleId,
                slotIndex,
                0
                );
        }
    }

    copied = Pv1bCopyReportToRequest(
        Request,
        report
        );

    if (!copied) {
        InterlockedIncrement64(
            &Ctx->Counters.DeferredBufferFailures
            );

        InterlockedIncrement64(
            &Ctx->Counters.FatalBypassEvents
            );

        Pv0112EnterBypass(
            Ctx,
            "DEFERRED-DELIVER-BUFFER-FAIL",
            RequestCtx->CycleId,
            FALSE
            );

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            STATUS_DATA_ERROR,
            0u,
            "DEFERRED-DELIVER-BUFFER-FAIL"
            );

        return TRUE;
    }

    InterlockedIncrement64(
        &Ctx->Counters.DeferredDeliveries
        );

    physicalIndex = slotIndex + 2u;
    completionEvent = Pv1bDeferredCompletionEvent(
        slotIndex
        );

    Pv1bLogEvent(
        "DEFERRED-SLOT-DELIVER",
        Ctx,
        RequestCtx->CycleId,
        physicalIndex,
        countAfter
        );

    Pv1bCompleteUpper(
        Request,
        RequestCtx,
        Ctx,
        STATUS_SUCCESS,
        information,
        completionEvent
        );

    return TRUE;
}

/*
 * PV0112: leave a recoverable bypass at the next safe idle boundary.
 *
 * While the bypass is on, the pipeline sees no report, so its FIFO and
 * episode state stay frozen at whatever the failure left (366.20 s:
 * deferredCount=1, episodeActive=1) and can never say when it is safe to
 * resume.  The stream can: a touch report with no tip - the test PV1K uses
 * to end an episode - that the reader has just received through the bypass
 * means the reader holds no contact.  Whatever the pipeline still holds is
 * older than that report, so it is dropped; the episode is then over and the
 * FIFO empty, and the next read starts a fresh cycle.
 *
 * Only the recoverable value is cleared (compare-exchange on exactly
 * PV0112_BYPASS_ON); a sticky bypass waits for EvtDeviceD0Entry as before.
 * Nothing is cleared while a request still owns the lookahead.
 */
static VOID
Pv0112TryLeaveBypass(
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ ULONG_PTR Information
    )
{
    LONG droppedDeferred;
    LONG droppedBoundary;
    LONG clearedEpisode;
    BOOLEAN left;

    droppedDeferred = 0;
    droppedBoundary = 0;
    clearedEpisode = 0;
    left = FALSE;

    WdfSpinLockAcquire(Ctx->StateLock);

    if (Ctx->ActiveLookaheadRequest == WDF_NO_HANDLE &&
        InterlockedCompareExchange(
            &Ctx->FatalBypass,
            0,
            PV0112_BYPASS_ON
            ) == PV0112_BYPASS_ON) {
        droppedDeferred = InterlockedExchange(&Ctx->DeferredValid, 0);
        droppedBoundary = InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
        Ctx->DeferredHead = 0u;
        clearedEpisode = InterlockedExchange(
            &Ctx->PhysicalEpisodeActive,
            0
            );
        InterlockedExchange(&Ctx->FutureVetoActive, 0);
        InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
        left = TRUE;
    }

    WdfSpinLockRelease(Ctx->StateLock);

    if (!left) {
        return;
    }

    InterlockedExchange64(&Ctx->LastRollingOutputQpc, 0);

    if (clearedEpisode != 0) {
        InterlockedIncrement64(&Ctx->Counters.EpisodeEnds);
    }

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_ERROR_LEVEL,
        "PalmRejPipeline PV0112: BYPASS-EXIT reason=IDLE-BOUNDARY dev=%ld info=%Iu droppedDeferred=%ld droppedBoundaryRelease=%ld episodeClosed=%ld fatalEvents=%I64d exits=%I64d\n",
        Ctx->DeviceId,
        Information,
        droppedDeferred,
        droppedBoundary,
        clearedEpisode,
        InterlockedCompareExchange64(&Ctx->Counters.FatalBypassEvents, 0, 0),
        InterlockedIncrement64(&g_V0112BypassExits)
        );
}

/*
 * PV0112: completion of a read forwarded under a recoverable bypass.  The
 * reader gets exactly what the lower driver returned, as with the PV1K
 * send-and-forget bypass; the newest whole report is only looked at on its
 * way up.
 */
VOID
EvtPv0112BypassCompletion(
    _In_ WDFREQUEST Request,
    _In_ WDFIOTARGET Target,
    _In_ PWDF_REQUEST_COMPLETION_PARAMS Params,
    _In_ WDFCONTEXT Context
    )
{
    PPV1B_REQUEST_CONTEXT requestCtx;
    PDEVICE_CONTEXT ctx;
    NTSTATUS status;
    ULONG_PTR information;
    NTSTATUS retrieveStatus;
    PVOID outputBuffer;
    size_t outputLength;
    UCHAR newest[PV1B_REPORT_BYTES];

    UNREFERENCED_PARAMETER(Target);

    requestCtx = (PPV1B_REQUEST_CONTEXT)Context;
    ctx = DeviceGetContext(
        requestCtx->Device
        );

    status = Params->IoStatus.Status;
    information = Params->IoStatus.Information;
    outputBuffer = NULL;
    outputLength = 0u;

    if (NT_SUCCESS(status) &&
        information >= PV1B_REPORT_BYTES &&
        (information % PV1B_REPORT_BYTES) == 0u) {
        retrieveStatus = Pv1bRetrieveOutput(
            Request,
            &outputBuffer,
            &outputLength
            );

        if (NT_SUCCESS(retrieveStatus) &&
            outputBuffer != NULL &&
            outputLength >= information) {
            RtlCopyMemory(
                newest,
                (const UCHAR*)outputBuffer + (information - PV1B_REPORT_BYTES),
                PV1B_REPORT_BYTES
                );

            // Cleared before the reader sees this report, so its next read
            // already goes through the pipeline.
            if (newest[0] == PV1B_REPORT_ID_TOUCH &&
                !Pv1bIsActiveTouchReport(newest)) {
                Pv0112TryLeaveBypass(
                    ctx,
                    information
                    );
            }
        }
    }

    Pv1bCompleteUpper(
        Request,
        requestCtx,
        ctx,
        status,
        information,
        "PV0112-BYPASS-PASS-THROUGH"
        );
}

static VOID
Pv0112ForwardBypassObserved(
    _In_ WDFDEVICE Device,
    _Inout_ PDEVICE_CONTEXT Ctx,
    _In_ WDFREQUEST Request,
    _Inout_ PPV1B_REQUEST_CONTEXT RequestCtx
    )
{
    BOOLEAN sent;
    NTSTATUS status;

    RequestCtx->Device = Device;

    WdfRequestFormatRequestUsingCurrentType(
        Request
        );

    WdfRequestSetCompletionRoutine(
        Request,
        EvtPv0112BypassCompletion,
        RequestCtx
        );

    // Same reference discipline as Pv1bSendFirst: Pv1bCompleteUpper drops it.
    WdfObjectReference(
        Device
        );

    InterlockedExchange(
        &RequestCtx->DeviceReferenceHeld,
        1
        );

    sent = WdfRequestSend(
        Request,
        WdfDeviceGetIoTarget(Device),
        WDF_NO_SEND_OPTIONS
        );

    if (sent == FALSE) {
        status = WdfRequestGetStatus(
            Request
            );

        InterlockedIncrement64(
            &Ctx->Counters.DirectForwardFailures
            );

        Pv1bCompleteUpper(
            Request,
            RequestCtx,
            Ctx,
            status,
            0u,
            "FATAL-BYPASS-DIRECT-FAIL"
            );

        return;
    }

    // No Request / RequestCtx access after a successful send.
    InterlockedIncrement64(
        &Ctx->Counters.DirectForwards
        );
}

VOID
EvtIoDefault(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request
    )
{
    WDFDEVICE device;
    PDEVICE_CONTEXT ctx;
    PPV1B_REQUEST_CONTEXT requestCtx;
    WDF_REQUEST_PARAMETERS parameters;
    LONG bypassState;

    device = WdfIoQueueGetDevice(
        Queue
        );

    ctx = DeviceGetContext(
        device
        );

    requestCtx = RequestGetContext(
        Request
        );

    RtlZeroMemory(
        requestCtx,
        sizeof(*requestCtx)
        );

    requestCtx->Device = device;

    WDF_REQUEST_PARAMETERS_INIT(
        &parameters
        );

    WdfRequestGetParameters(
        Request,
        &parameters
        );

    if (!Pv1bIsInputReportRequest(
            &parameters
            )) {
        Pv1bForwardDirect(
            device,
            ctx,
            Request,
            "NON-INPUT-DIRECT-FAIL"
            );

        return;
    }

    InterlockedIncrement64(
        &ctx->Counters.InputCandidates
        );

    bypassState = InterlockedCompareExchange(
        &ctx->FatalBypass,
        0,
        0
        );

    if (bypassState != 0) {
        // PV0112: a sticky bypass is forwarded exactly as PV1K did.  A
        // recoverable one is forwarded unchanged too, but its completion is
        // watched for the all-up report that lets the pipeline resume.
        if ((bypassState & PV0112_BYPASS_STICKY) != 0) {
            Pv1bForwardDirect(
                device,
                ctx,
                Request,
                "FATAL-BYPASS-DIRECT-FAIL"
                );
        }
        else {
            Pv0112ForwardBypassObserved(
                device,
                ctx,
                Request,
                requestCtx
                );
        }

        return;
    }

    // The only completion allowed without starting a new physical lower read
    // is the single real release captured during initial warm-up. Active
    // reports are never drained here.
    if (Pv1dTryDeliverBoundaryRelease(
            ctx,
            Request,
            requestCtx
            )) {
        return;
    }

    Pv1bSendFirst(
        device,
        ctx,
        Request,
        requestCtx
        );
}

static NTSTATUS
Pv1bCreateObjects(
    _In_ WDFDEVICE Device,
    _Inout_ PDEVICE_CONTEXT Ctx
    )
{
    NTSTATUS status;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_OBJECT_ATTRIBUTES attributes;

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(
        &queueConfig,
        WdfIoQueueDispatchParallel
        );

    queueConfig.EvtIoDefault = EvtIoDefault;
    queueConfig.PowerManaged = WdfFalse;

    WDF_OBJECT_ATTRIBUTES_INIT(
        &attributes
        );

    status = WdfIoQueueCreate(
        Device,
        &queueConfig,
        &attributes,
        &Ctx->DefaultQueue
        );

    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_OBJECT_ATTRIBUTES_INIT(
        &attributes
        );

    attributes.ParentObject = Device;

    status = WdfSpinLockCreate(
        &attributes,
        &Ctx->StateLock
        );

    if (!NT_SUCCESS(status)) {
        return status;
    }

    Ctx->ActiveLookaheadRequest = WDF_NO_HANDLE;
    Ctx->DeferredHead = 0u;
    InterlockedExchange(&Ctx->DeferredValid, 0);
    InterlockedExchange(&Ctx->BoundaryReleasePending, 0);
    RtlZeroMemory(Ctx->LastUpperTouchReport, PV1B_REPORT_BYTES);
    Ctx->LastUpperTouchInformation = 0u;
    InterlockedExchange(&Ctx->LastUpperTouchValid, 0);
    InterlockedExchange64(&Ctx->LastRollingOutputQpc, 0);
    InterlockedExchange(&Ctx->PhysicalEpisodeActive, 0);
    InterlockedExchange(&Ctx->FutureVetoActive, 0);
    InterlockedExchange(&Ctx->FutureVetoReasonMask, 0);
    InterlockedExchange64(&Ctx->EpisodeId, 0);

    return STATUS_SUCCESS;
}

NTSTATUS
EvtDevicePrepareHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesRaw,
    _In_ WDFCMRESLIST ResourcesTranslated
    )
{
    PDEVICE_CONTEXT ctx;

    UNREFERENCED_PARAMETER(ResourcesRaw);
    UNREFERENCED_PARAMETER(ResourcesTranslated);

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.PrepareHardwareCalls
        );

    InterlockedExchange(
        &ctx->HardwarePrepared,
        1
        );

    InterlockedExchange(
        &ctx->SurpriseRemoved,
        0
        );

    Pv1bLogEvent(
        "PREPARE-HARDWARE",
        ctx,
        0,
        0,
        0
        );

    return STATUS_SUCCESS;
}

NTSTATUS
EvtDeviceReleaseHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesTranslated
    )
{
    PDEVICE_CONTEXT ctx;

    UNREFERENCED_PARAMETER(ResourcesTranslated);

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.ReleaseHardwareCalls
        );

    InterlockedExchange(
        &ctx->HardwarePrepared,
        0
        );

    InterlockedExchange(
        &ctx->DeviceInD0,
        0
        );

    Pv1bLogEvent(
        "RELEASE-HARDWARE",
        ctx,
        0,
        0,
        0
        );

    return STATUS_SUCCESS;
}

NTSTATUS
EvtDeviceD0Entry(
    _In_ WDFDEVICE Device,
    _In_ WDF_POWER_DEVICE_STATE PreviousState
    )
{
    PDEVICE_CONTEXT ctx;
    LONG previousBypass;

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.D0EntryCalls
        );

    InterlockedIncrement(
        &ctx->Generation
        );

    InterlockedExchange(
        &ctx->DeviceInD0,
        1
        );

    previousBypass = InterlockedExchange(
        &ctx->FatalBypass,
        0
        );

    if (previousBypass != 0) {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "PalmRejPipeline PV0112: BYPASS-EXIT reason=D0-ENTRY dev=%ld was=0x%lX fatalEvents=%I64d exits=%I64d\n",
            ctx->DeviceId,
            (ULONG)previousBypass,
            InterlockedCompareExchange64(&ctx->Counters.FatalBypassEvents, 0, 0),
            InterlockedIncrement64(&g_V0112BypassExits)
            );
    }

    InterlockedExchange(
        &ctx->DeferredValid,
        0
        );

    InterlockedExchange(
        &ctx->BoundaryReleasePending,
        0
        );

    ctx->DeferredHead = 0u;

    InterlockedExchange64(
        &ctx->LastRollingOutputQpc,
        0
        );

    InterlockedExchange(
        &ctx->PhysicalEpisodeActive,
        0
        );
    InterlockedExchange(&ctx->FutureVetoActive, 0);
    InterlockedExchange(&ctx->FutureVetoReasonMask, 0);

    Pv1bLogEvent(
        "D0-ENTRY",
        ctx,
        PreviousState,
        0,
        0
        );

    return STATUS_SUCCESS;
}

NTSTATUS
EvtDeviceD0Exit(
    _In_ WDFDEVICE Device,
    _In_ WDF_POWER_DEVICE_STATE TargetState
    )
{
    PDEVICE_CONTEXT ctx;
    LONG deferredCount;

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.D0ExitCalls
        );

    Pv1bCancelActiveLookahead(
        ctx,
        "D0-EXIT-CANCEL-ACTIVE-LOOKAHEAD"
        );

    InterlockedExchange(
        &ctx->DeviceInD0,
        0
        );

    deferredCount = InterlockedExchange(
        &ctx->DeferredValid,
        0
        );

    InterlockedExchange(
        &ctx->BoundaryReleasePending,
        0
        );

    if (deferredCount > 0) {
        InterlockedExchangeAdd64(
            &ctx->Counters.TeardownDeferredDrops,
            deferredCount
            );

        Pv1bLogEvent(
            "D0-EXIT-DROPPED-DEFERRED-TAIL",
            ctx,
            deferredCount,
            0,
            0
            );
    }

    ctx->DeferredHead = 0u;

    InterlockedExchange64(
        &ctx->LastRollingOutputQpc,
        0
        );

    InterlockedExchange(
        &ctx->PhysicalEpisodeActive,
        0
        );
    InterlockedExchange(&ctx->FutureVetoActive, 0);
    InterlockedExchange(&ctx->FutureVetoReasonMask, 0);

    Pv1bLogEvent(
        "D0-EXIT",
        ctx,
        TargetState,
        deferredCount,
        0
        );

    return STATUS_SUCCESS;
}

NTSTATUS
EvtDeviceSelfManagedIoInit(
    _In_ WDFDEVICE Device
    )
{
    PDEVICE_CONTEXT ctx;

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.SelfManagedInitCalls
        );

    InterlockedExchange(
        &ctx->SelfManagedActive,
        1
        );

    Pv1bLogEvent(
        "SMIO-INIT",
        ctx,
        0,
        0,
        0
        );

    return STATUS_SUCCESS;
}

NTSTATUS
EvtDeviceSelfManagedIoSuspend(
    _In_ WDFDEVICE Device
    )
{
    PDEVICE_CONTEXT ctx;

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.SelfManagedSuspendCalls
        );

    InterlockedExchange(
        &ctx->SelfManagedActive,
        0
        );

    Pv1bCancelActiveLookahead(
        ctx,
        "SMIO-SUSPEND-CANCEL-ACTIVE-LOOKAHEAD"
        );

    Pv1bLogEvent(
        "SMIO-SUSPEND",
        ctx,
        0,
        0,
        0
        );

    return STATUS_SUCCESS;
}

NTSTATUS
EvtDeviceSelfManagedIoRestart(
    _In_ WDFDEVICE Device
    )
{
    PDEVICE_CONTEXT ctx;

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.SelfManagedRestartCalls
        );

    InterlockedExchange(
        &ctx->SelfManagedActive,
        1
        );

    Pv1bLogEvent(
        "SMIO-RESTART",
        ctx,
        0,
        0,
        0
        );

    return STATUS_SUCCESS;
}

VOID
EvtDeviceSelfManagedIoCleanup(
    _In_ WDFDEVICE Device
    )
{
    PDEVICE_CONTEXT ctx;

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.SelfManagedCleanupCalls
        );

    InterlockedExchange(
        &ctx->SelfManagedActive,
        0
        );

    Pv1bLogEvent(
        "SMIO-CLEANUP-SCALARS-ONLY",
        ctx,
        0,
        0,
        0
        );
}

VOID
EvtDeviceSurpriseRemoval(
    _In_ WDFDEVICE Device
    )
{
    PDEVICE_CONTEXT ctx;
    LONG deferredCount;

    ctx = DeviceGetContext(
        Device
        );

    InterlockedIncrement64(
        &ctx->Counters.SurpriseRemovalCalls
        );

    InterlockedExchange(
        &ctx->SurpriseRemoved,
        1
        );

    Pv1bCancelActiveLookahead(
        ctx,
        "SURPRISE-REMOVAL-CANCEL-ACTIVE-LOOKAHEAD"
        );

    InterlockedExchange(
        &ctx->DeviceInD0,
        0
        );

    InterlockedExchange(
        &ctx->SelfManagedActive,
        0
        );

    deferredCount = InterlockedExchange(
        &ctx->DeferredValid,
        0
        );

    InterlockedExchange(
        &ctx->BoundaryReleasePending,
        0
        );

    if (deferredCount > 0) {
        InterlockedExchangeAdd64(
            &ctx->Counters.TeardownDeferredDrops,
            deferredCount
            );

        Pv1bLogEvent(
            "SURPRISE-REMOVAL-DROPPED-DEFERRED-TAIL",
            ctx,
            deferredCount,
            0,
            0
            );
    }

    ctx->DeferredHead = 0u;

    InterlockedExchange64(
        &ctx->LastRollingOutputQpc,
        0
        );

    InterlockedExchange(
        &ctx->PhysicalEpisodeActive,
        0
        );
    InterlockedExchange(&ctx->FutureVetoActive, 0);
    InterlockedExchange(&ctx->FutureVetoReasonMask, 0);

    Pv1bLogEvent(
        "SURPRISE-REMOVAL",
        ctx,
        deferredCount,
        0,
        0
        );
}

VOID
EvtDeviceContextCleanup(
    _In_ WDFOBJECT DeviceObject
    )
{
    WDFDEVICE device;
    PDEVICE_CONTEXT ctx;

    device = (WDFDEVICE)DeviceObject;
    ctx = DeviceGetContext(
        device
        );

    InterlockedIncrement64(
        &ctx->Counters.ContextCleanupCalls
        );

    DbgPrint(
        "PalmRejPipeline PV1I: CLEANUP-SUMMARY deviceId=%ld cycles=%I64d firstComp=%I64d firstActive=%I64d lookReuse=%I64d lookReuseOK=%I64d lookSend=%I64d lookComp=%I64d lookCanceled=%I64d deferredStore=%I64d deferredDeliver=%I64d deferredCollision=%I64d rollingPhysical=%I64d rollingOutput=%I64d rollingClear=%I64d rollingBoundaryFlush=%I64d boundaryTailDrops=%I64d stalePurges=%I64d rollingInvariant=%I64d rollingCadenceViolation=%I64d boundaryStore=%I64d boundaryDeliver=%I64d boundaryInvariant=%I64d episodeStarts=%I64d episodeEnds=%I64d episodeStartFail=%I64d staleBoundary=%I64d tailDrain=%I64d teardownDeferredDrops=%I64d upperComp=%I64d upperCanceled=%I64d doublePrevented=%I64d cancelAttempts=%I64d cancelAccepted=%I64d cancelRejected=%I64d lookFoPreserved=%I64d fatal=%I64d activeLookahead=%p deferredCount=%ld boundaryRelease=%ld\n",
        ctx->DeviceId,
        InterlockedCompareExchange64(&ctx->Counters.CyclesStarted,0,0),
        InterlockedCompareExchange64(&ctx->Counters.FirstCompletions,0,0),
        InterlockedCompareExchange64(&ctx->Counters.FirstActiveReports,0,0),
        InterlockedCompareExchange64(&ctx->Counters.LookaheadReuseAttempts,0,0),
        InterlockedCompareExchange64(&ctx->Counters.LookaheadReuseSuccesses,0,0),
        InterlockedCompareExchange64(&ctx->Counters.LookaheadSendAttempts,0,0),
        InterlockedCompareExchange64(&ctx->Counters.LookaheadCompletions,0,0),
        InterlockedCompareExchange64(&ctx->Counters.LookaheadCompletionCanceled,0,0),
        InterlockedCompareExchange64(&ctx->Counters.DeferredStores,0,0),
        InterlockedCompareExchange64(&ctx->Counters.DeferredDeliveries,0,0),
        InterlockedCompareExchange64(&ctx->Counters.DeferredCollisionFailures,0,0),
        InterlockedCompareExchange64(&ctx->Counters.RollingPhysicalArrivals,0,0),
        InterlockedCompareExchange64(&ctx->Counters.RollingOutputs,0,0),
        InterlockedCompareExchange64(&ctx->Counters.RollingAllNonactiveClears,0,0),
        InterlockedCompareExchange64(&ctx->Counters.RollingBoundaryFlushes,0,0),
        InterlockedCompareExchange64(&ctx->Counters.BoundaryActiveTailDrops,0,0),
        InterlockedCompareExchange64(&ctx->Counters.StaleFifoPurgesBeforeNewEpisode,0,0),
        InterlockedCompareExchange64(&ctx->Counters.RollingInvariantFailures,0,0),
        InterlockedCompareExchange64(&ctx->Counters.RollingCadenceViolations,0,0),
        InterlockedCompareExchange64(&ctx->Counters.BoundaryReleaseStores,0,0),
        InterlockedCompareExchange64(&ctx->Counters.BoundaryReleaseDeliveries,0,0),
        InterlockedCompareExchange64(&ctx->Counters.BoundaryReleaseInvariantFailures,0,0),
        InterlockedCompareExchange64(&ctx->Counters.EpisodeStarts,0,0),
        InterlockedCompareExchange64(&ctx->Counters.EpisodeEnds,0,0),
        InterlockedCompareExchange64(&ctx->Counters.EpisodeStartInvariantFailures,0,0),
        InterlockedCompareExchange64(&ctx->Counters.EpisodeBoundaryStaleFifoFailures,0,0),
        InterlockedCompareExchange64(&ctx->Counters.TailDrainCompletions,0,0),
        InterlockedCompareExchange64(&ctx->Counters.TeardownDeferredDrops,0,0),
        InterlockedCompareExchange64(&ctx->Counters.UpperCompletions,0,0),
        InterlockedCompareExchange64(&ctx->Counters.UpperCanceledCompletions,0,0),
        InterlockedCompareExchange64(&ctx->Counters.DoubleCompletePrevented,0,0),
        InterlockedCompareExchange64(&ctx->Counters.CancelSentAttempts,0,0),
        InterlockedCompareExchange64(&ctx->Counters.CancelSentAccepted,0,0),
        InterlockedCompareExchange64(&ctx->Counters.CancelSentRejected,0,0),
        InterlockedCompareExchange64(&ctx->Counters.LookaheadFileObjectPreserved,0,0),
        InterlockedCompareExchange64(&ctx->Counters.FatalBypassEvents,0,0),
        ctx->ActiveLookaheadRequest,
        InterlockedCompareExchange(&ctx->DeferredValid,0,0),
        InterlockedCompareExchange(&ctx->BoundaryReleasePending,0,0)
        );
}


NTSTATUS
EvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
    )
{
    NTSTATUS status;
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_OBJECT_ATTRIBUTES requestAttributes;
    WDF_PNPPOWER_EVENT_CALLBACKS callbacks;
    PDEVICE_CONTEXT ctx;

    UNREFERENCED_PARAMETER(Driver);

    WdfFdoInitSetFilter(
        DeviceInit
        );

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(
        &requestAttributes,
        PV1B_REQUEST_CONTEXT
        );

    WdfDeviceInitSetRequestAttributes(
        DeviceInit,
        &requestAttributes
        );

    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(
        &callbacks
        );

    callbacks.EvtDevicePrepareHardware =
        EvtDevicePrepareHardware;
    callbacks.EvtDeviceReleaseHardware =
        EvtDeviceReleaseHardware;
    callbacks.EvtDeviceD0Entry =
        EvtDeviceD0Entry;
    callbacks.EvtDeviceD0Exit =
        EvtDeviceD0Exit;
    callbacks.EvtDeviceSelfManagedIoInit =
        EvtDeviceSelfManagedIoInit;
    callbacks.EvtDeviceSelfManagedIoSuspend =
        EvtDeviceSelfManagedIoSuspend;
    callbacks.EvtDeviceSelfManagedIoRestart =
        EvtDeviceSelfManagedIoRestart;
    callbacks.EvtDeviceSelfManagedIoCleanup =
        EvtDeviceSelfManagedIoCleanup;
    callbacks.EvtDeviceSurpriseRemoval =
        EvtDeviceSurpriseRemoval;

    WdfDeviceInitSetPnpPowerEventCallbacks(
        DeviceInit,
        &callbacks
        );

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(
        &attributes,
        DEVICE_CONTEXT
        );

    attributes.EvtCleanupCallback =
        EvtDeviceContextCleanup;

    attributes.ExecutionLevel =
        WdfExecutionLevelPassive;

    status = WdfDeviceCreate(
        &DeviceInit,
        &attributes,
        &device
        );

    if (!NT_SUCCESS(status)) {
        return status;
    }

    ctx = DeviceGetContext(
        device
        );

    RtlZeroMemory(
        ctx,
        sizeof(*ctx)
        );

    ctx->DeviceId = InterlockedIncrement(
        &g_Pv1bNextDeviceId
        );

    ctx->Role =
        Pv1bRoleTouchCol02;

    status = Pv1bCreateObjects(
        device,
        ctx
        );

    if (!NT_SUCCESS(status)) {
        return status;
    }

    Pv1bLogEvent(
        "DEVICE-ADD-EIGHT-REPORT-LOOKAHEAD",
        ctx,
        0,
        0,
        0
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
    NTSTATUS status;

    WDF_DRIVER_CONFIG_INIT(
        &config,
        EvtDeviceAdd
        );

    status = WdfDriverCreate(
        DriverObject,
        RegistryPath,
        WDF_NO_OBJECT_ATTRIBUTES,
        &config,
        WDF_NO_HANDLE
        );

    Pv1bLogEvent(
        "DRIVER-ENTRY-EIGHT-REPORT-LOOKAHEAD",
        NULL,
        status,
        PV1B_DRIVER_VERSION_MAJOR,
        PV1B_DRIVER_VERSION_MINOR
        );

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_ERROR_LEVEL,
        "PalmRejPipeline BUILD: PalmRej-0.1.12 base=PV1K lookahead-coherent-release batch-safe\n"
        );

    return status;
}
