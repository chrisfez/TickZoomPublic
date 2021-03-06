; Listing generated by Microsoft (R) Optimizing Compiler Version 15.00.30729.01 

	TITLE	c:\Local\SharpDevelop_3.2.0.5777_Source\src\AddIns\Misc\Profiler\Hook\SharedMemory.cpp
	.686P
	.XMM
	include listing.inc
	.model	flat

INCLUDELIB OLDNAMES

PUBLIC	??_C@_0DL@FKOGONME@Could?5not?5open?5Shared?5Memory?0?5pl@ ; `string'
PUBLIC	??_C@_0CL@JNLEPBOA@Wrong?5build?5configuration?$DL?5RELEA@ ; `string'
PUBLIC	??_C@_0EI@IBFBEDOB@Error?5while?5creating?5temporary?5s@ ; `string'
EXTRN	__imp__MapViewOfFile@20:PROC
EXTRN	__imp__UnmapViewOfFile@4:PROC
EXTRN	__imp__OpenFileMappingA@12:PROC
EXTRN	__imp__CloseHandle@4:PROC
;	COMDAT ??_C@_0EI@IBFBEDOB@Error?5while?5creating?5temporary?5s@
CONST	SEGMENT
??_C@_0EI@IBFBEDOB@Error?5while?5creating?5temporary?5s@ DB 'Error while '
	DB	'creating temporary storage file (shared memory)!', 0aH, 0aH, 'E'
	DB	'rror: %d', 00H				; `string'
CONST	ENDS
;	COMDAT ??_C@_0CL@JNLEPBOA@Wrong?5build?5configuration?$DL?5RELEA@
CONST	SEGMENT
??_C@_0CL@JNLEPBOA@Wrong?5build?5configuration?$DL?5RELEA@ DB 'Wrong buil'
	DB	'd configuration; RELEASE needed!', 00H	; `string'
CONST	ENDS
;	COMDAT ??_C@_0DL@FKOGONME@Could?5not?5open?5Shared?5Memory?0?5pl@
CONST	SEGMENT
??_C@_0DL@FKOGONME@Could?5not?5open?5Shared?5Memory?0?5pl@ DB 'Could not '
	DB	'open Shared Memory, please restart the profiler!', 00H ; `string'
__bad_alloc_Message DD FLAT:??_C@_0P@GHFPNOJB@bad?5allocation?$AA@
PUBLIC	?GetStartPtr@CSharedMemory@@QAEPAXXZ		; CSharedMemory::GetStartPtr
; Function compile flags: /Ogtpy
; File c:\local\sharpdevelop_3.2.0.5777_source\src\addins\misc\profiler\hook\sharedmemory.cpp
;	COMDAT ?GetStartPtr@CSharedMemory@@QAEPAXXZ
_TEXT	SEGMENT
?GetStartPtr@CSharedMemory@@QAEPAXXZ PROC		; CSharedMemory::GetStartPtr, COMDAT
; _this$ = eax

; 59   : 	return this->startPtr;

	mov	eax, DWORD PTR [eax+8]

; 60   : }

	ret	0
?GetStartPtr@CSharedMemory@@QAEPAXXZ ENDP		; CSharedMemory::GetStartPtr
_TEXT	ENDS
PUBLIC	??0CSharedMemory@@QAE@PAD@Z			; CSharedMemory::CSharedMemory
; Function compile flags: /Ogtpy
;	COMDAT ??0CSharedMemory@@QAE@PAD@Z
_TEXT	SEGMENT
_buffer$87914 = -512					; size = 512
??0CSharedMemory@@QAE@PAD@Z PROC			; CSharedMemory::CSharedMemory, COMDAT
; _this$ = esi
; _name$ = eax

; 12   : {

	sub	esp, 512				; 00000200H
	push	ebx
	push	ebp
	push	edi

; 13   : 	this->fileHandle = OpenFileMapping(FILE_MAP_ALL_ACCESS, false, name);

	push	eax
	push	0
	push	983071					; 000f001fH
	call	DWORD PTR __imp__OpenFileMappingA@12

; 14   : 	if (fileHandle == nullptr) {
; 15   : 		DebugWriteLine(L"OpenFileMapping returned nullptr");
; 16   : 	}
; 17   : 	this->startPtr = MapViewOfFile(this->fileHandle, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedMemoryHeader));

	mov	ebx, DWORD PTR __imp__MapViewOfFile@20
	push	192					; 000000c0H
	push	0
	push	0
	push	983071					; 000f001fH
	push	eax
	mov	DWORD PTR [esi+4], eax
	call	ebx

; 18   : 	if (startPtr == nullptr) {

	mov	ebp, DWORD PTR __imp__MessageBoxA@16
	mov	DWORD PTR [esi+8], eax
	test	eax, eax
	jne	SHORT $LN4@CSharedMem

; 19   : 		DebugWriteLine(L"MapViewOfFile returned nullptr");
; 20   : 		MessageBox(nullptr, TEXT("Could not open Shared Memory, please restart the profiler!"), TEXT("Profiler Error"), MB_OK);

	push	0
	push	OFFSET ??_C@_0P@NHBFFJJG@Profiler?5Error?$AA@
	push	OFFSET ??_C@_0DL@FKOGONME@Could?5not?5open?5Shared?5Memory?0?5pl@
	push	0
	call	ebp
$LN4@CSharedMem:

; 21   : 	}
; 22   : 	SharedMemoryHeader *header = (SharedMemoryHeader*)this->startPtr;

	mov	edi, DWORD PTR [esi+8]

; 23   : 	#ifdef DEBUG
; 24   : 	if (header->Magic != '~DBG') {
; 25   : 		DebugWriteLine(L"Corrupted shared memory header");
; 26   : 		if (header->Magic == '~SM1') {
; 27   : 			MessageBox(nullptr, TEXT("Wrong build configuration; DEBUG needed!"), TEXT("Profiler Error"), MB_OK);
; 28   : 		}
; 29   : 	}
; 30   : 	#else
; 31   : 	if (header->Magic != '~SM1') {

	mov	eax, DWORD PTR [edi]
	cmp	eax, 2119388465				; 7e534d31H
	je	SHORT $LN2@CSharedMem

; 32   : 		DebugWriteLine(L"Corrupted shared memory header");
; 33   : 		if (header->Magic == '~DBG') {

	cmp	eax, 2118402631				; 7e444247H
	jne	SHORT $LN2@CSharedMem

; 34   : 			MessageBox(nullptr, TEXT("Wrong build configuration; RELEASE needed!"), TEXT("Profiler Error"), MB_OK);

	push	0
	push	OFFSET ??_C@_0P@NHBFFJJG@Profiler?5Error?$AA@
	push	OFFSET ??_C@_0CL@JNLEPBOA@Wrong?5build?5configuration?$DL?5RELEA@
	push	0
	call	ebp
$LN2@CSharedMem:

; 35   : 		}
; 36   : 	}
; 37   : 	#endif
; 38   : 	this->length = header->TotalLength;
; 39   : 	UnmapViewOfFile(this->startPtr);

	mov	edx, DWORD PTR [esi+8]
	mov	ecx, DWORD PTR [edi+8]
	push	edx
	mov	DWORD PTR [esi+12], ecx
	call	DWORD PTR __imp__UnmapViewOfFile@4

; 40   : 	DebugWriteLine(L"Length: %d", this->length);
; 41   : 	this->startPtr = MapViewOfFile(this->fileHandle, FILE_MAP_ALL_ACCESS, 0, 0, this->length);

	mov	eax, DWORD PTR [esi+12]
	mov	ecx, DWORD PTR [esi+4]
	push	eax
	push	0
	push	0
	push	983071					; 000f001fH
	push	ecx
	call	ebx
	mov	DWORD PTR [esi+8], eax

; 42   : 	if (startPtr == nullptr) {

	test	eax, eax
	jne	SHORT $LN1@CSharedMem

; 43   : 		char buffer[512];
; 44   : 		sprintf_s(buffer, 512, "Error while creating temporary storage file (shared memory)!\n\nError: %d", GetLastError());

	call	DWORD PTR __imp__GetLastError@0
	push	eax
	push	OFFSET ??_C@_0EI@IBFBEDOB@Error?5while?5creating?5temporary?5s@
	lea	edx, DWORD PTR _buffer$87914[esp+532]
	push	512					; 00000200H
	push	edx
	call	DWORD PTR __imp__sprintf_s
	add	esp, 16					; 00000010H

; 45   : 		DebugWriteLine(L"second MapViewOfFile returned nullptr");
; 46   : 		MessageBox(nullptr, buffer, TEXT("Profiler Error"), MB_OK);

	push	0
	push	OFFSET ??_C@_0P@NHBFFJJG@Profiler?5Error?$AA@
	lea	eax, DWORD PTR _buffer$87914[esp+532]
	push	eax
	push	0
	call	ebp
$LN1@CSharedMem:

; 47   : 	}
; 48   : 	this->header = (SharedMemoryHeader*)this->startPtr;

	mov	ecx, DWORD PTR [esi+8]
	pop	edi
	pop	ebp
	mov	DWORD PTR [esi], ecx

; 49   : }

	mov	eax, esi
	pop	ebx
	add	esp, 512				; 00000200H
	ret	0
??0CSharedMemory@@QAE@PAD@Z ENDP			; CSharedMemory::CSharedMemory
END
