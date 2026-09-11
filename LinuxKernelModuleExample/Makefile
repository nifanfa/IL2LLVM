obj-m += my_module.o
managed_object := bin/Debug/net10.0/LinuxKernelModuleExample.o
managed_input := bin/Debug/net10.0/LinuxKernelModuleExample.obj
my_module-objs := my_module_main.o Runtime.o runtime_jump_x86_64.o $(managed_object)

PWD  := $(shell pwd)

KDIRS := $(wildcard /lib/modules/*/build)

all:
	$(MAKE) -C $(firstword $(KDIRS)) M=$(PWD) modules

clean:
	@for kdir in $(KDIRS); do \
		$(MAKE) -C $$kdir M=$(PWD) clean; \
	done

$(obj)/$(managed_object): $(src)/$(managed_input)
	cp $< $@
