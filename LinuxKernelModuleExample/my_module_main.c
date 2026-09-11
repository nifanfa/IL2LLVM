#include <linux/init.h>
#include <linux/module.h>
#include <linux/kernel.h>
#include <linux/kthread.h>

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

module_init(my_module_init);
module_exit(my_module_exit);
