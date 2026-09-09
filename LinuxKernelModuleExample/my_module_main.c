#include <linux/init.h>
#include <linux/module.h>
#include <linux/kernel.h>
#include <linux/kthread.h>
#include <linux/nls.h>

MODULE_LICENSE("GPL");
MODULE_AUTHOR("Your Name");
MODULE_DESCRIPTION("A Simple Linux Kernel Module");
MODULE_VERSION("1.0");

extern void managed_Main(void);

int my_thread(void* dummy){
    managed_Main();
    return 0;
}

static int __init my_module_init(void)
{
    kthread_run(my_thread, NULL, "my_thread"); // Running it in a thread prevents insmod from blocking if a loop occurs
    printk(KERN_INFO "My Module: Loaded successfully!\n");
    return 0;
}

static void __exit my_module_exit(void)
{
    printk(KERN_INFO "My Module: Unloaded successfully!\n");
}

void System_Console_Write_System_ReadOnlySpan_1_System_Byte(const char* str)
{
    if (str == NULL)
        return;
    printk(KERN_INFO "%s", str);
}

void System_Console_WriteLine_System_ReadOnlySpan_1_System_Byte(const char* str)
{
    System_Console_Write_System_ReadOnlySpan_1_System_Byte(str);
    printk(KERN_INFO "\n");
}

void System_Console_Write_String(const short *str)
{
    char utf8_buf[256];
    int src_len = 0;
    int len;
    if (str == NULL)
        return;
    while (str[src_len] != 0 && src_len < sizeof(utf8_buf) / 2)
    {
        src_len++;
    }
    len = utf16s_to_utf8s((const u16 *)str, src_len, UTF16_HOST_ENDIAN, utf8_buf, sizeof(utf8_buf) - 1);
    utf8_buf[len] = '\0';
    printk(KERN_INFO "%s\n", utf8_buf);
}

void System_Console_WriteLine_String(const short *str)
{
    System_Console_Write_String(str);
    printk(KERN_INFO "\n");
}

void System_Console_WriteLine_Int32(int value)
{
    printk(KERN_INFO "%d\n", value);
}

void System_Console_WriteLine_IntPtr(size_t value)
{
    printk(KERN_INFO "%zu\n", value);
}

void System_Threading_Monitor_Enter_Object(void *obj)
{
    // TO-DO
}

void System_Threading_Monitor_Exit_Object(void *obj)
{
    // TO-DO
}

void free(void* ptr){
    vfree(ptr);
}

void* calloc(size_t num, size_t size){
    void* ptr = vmalloc(num * size);
    memset(ptr,0,  num * size);
    return ptr;
}

void abort(void)
{
    panic("Managed runtime aborted");
}

module_init(my_module_init);
module_exit(my_module_exit);
