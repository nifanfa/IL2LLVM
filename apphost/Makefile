PROGRAM := ConsoleAppExample
MANAGED_OBJECT := ../ConsoleAppExample/bin/Debug/net10.0/ConsoleAppExample.obj
SOURCES := apphost.c CoreLib.linux.c

CFLAGS ?= -O2 -Wall -Wextra -fno-pie
LDFLAGS ?= -no-pie

all: $(PROGRAM)

$(PROGRAM): $(SOURCES) $(MANAGED_OBJECT)
	$(CC) $(CFLAGS) $(LDFLAGS) -o $@ $^ $(LDLIBS)

clean:
	rm -f $(PROGRAM)

.PHONY: all clean
