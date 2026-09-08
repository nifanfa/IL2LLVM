.intel_syntax noprefix
.text

.equ JUMP_BUFFER_RBX, 0
.equ JUMP_BUFFER_RBP, 8
.equ JUMP_BUFFER_R12, 16
.equ JUMP_BUFFER_R13, 24
.equ JUMP_BUFFER_R14, 32
.equ JUMP_BUFFER_R15, 40
.equ JUMP_BUFFER_RSP, 48
.equ JUMP_BUFFER_RIP, 56

.globl setjmp
setjmp:
	mov [rdi + JUMP_BUFFER_RBX], rbx
	mov [rdi + JUMP_BUFFER_RBP], rbp
	mov [rdi + JUMP_BUFFER_R12], r12
	mov [rdi + JUMP_BUFFER_R13], r13
	mov [rdi + JUMP_BUFFER_R14], r14
	mov [rdi + JUMP_BUFFER_R15], r15
	lea rax, [rsp + 8]
	mov [rdi + JUMP_BUFFER_RSP], rax
	mov rax, [rsp]
	mov [rdi + JUMP_BUFFER_RIP], rax
	xor eax, eax
	ret

.globl longjmp
longjmp:
	mov rbx, [rdi + JUMP_BUFFER_RBX]
	mov rbp, [rdi + JUMP_BUFFER_RBP]
	mov r12, [rdi + JUMP_BUFFER_R12]
	mov r13, [rdi + JUMP_BUFFER_R13]
	mov r14, [rdi + JUMP_BUFFER_R14]
	mov r15, [rdi + JUMP_BUFFER_R15]
	mov eax, esi
	test eax, eax
	jne .longjmp_value_ready
	mov eax, 1
.longjmp_value_ready:
	mov rsp, [rdi + JUMP_BUFFER_RSP]
	jmp qword ptr [rdi + JUMP_BUFFER_RIP]
