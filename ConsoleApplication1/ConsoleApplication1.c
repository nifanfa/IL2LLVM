// ConsoleApplication1.cpp : 此文件包含 "main" 函数。程序执行将在此处开始并结束。
//

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <stdbool.h>

void Program_Main_Array(void* args);
int Program_ForTest(void);
int Program_ArrayTest(void);
void Program_ObjectTest(void);
int Program_CalculateTest(void);
void Program_BranchTest(void);
int Program_StaticFieldTest(void);

int main()
{
	printf("Return value: %d\n", Program_StaticFieldTest());
	Program_Main_Array(NULL);
	printf("Return value: %d\n", Program_ForTest());
	printf("Return value: %d\n", Program_ArrayTest());
	Program_ObjectTest();
	printf("Return value: %d\n", Program_CalculateTest());
	Program_BranchTest();
}

void System_Console_Write_String(const char* str)
{
	printf("%s", str);
}

void System_Console_WriteLine_String(const char* str)
{
	printf("%s\n", str);
}

void System_Object__ctor(void* ptr)
{
	// do nothing
}

void* Newobj(size_t size) {
	return calloc(1, size);
}

void* Newarr(size_t count, size_t size) {
	return calloc(count, size);
}

// 运行程序: Ctrl + F5 或调试 >“开始执行(不调试)”菜单
// 调试程序: F5 或调试 >“开始调试”菜单

// 入门使用技巧: 
//   1. 使用解决方案资源管理器窗口添加/管理文件
//   2. 使用团队资源管理器窗口连接到源代码管理
//   3. 使用输出窗口查看生成输出和其他消息
//   4. 使用错误列表窗口查看错误
//   5. 转到“项目”>“添加新项”以创建新的代码文件，或转到“项目”>“添加现有项”以将现有代码文件添加到项目
//   6. 将来，若要再次打开此项目，请转到“文件”>“打开”>“项目”并选择 .sln 文件
