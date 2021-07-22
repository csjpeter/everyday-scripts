#!/bin/bash
if sudo smartctl -a /dev/sda | grep "^Device Model:" | grep "Samsung SSD 850 EVO 1TB"; then
	sudo smartctl -A /dev/sda | grep "^241"
	sudo smartctl -A /dev/sda | awk '/^241/ { print "/dev/sda TBW: "($10 * 512) * 1.0e-12, "TB" }'
elif sudo smartctl -a /dev/sda | grep "^Device Model:" | grep "KINGSTON SKC400S371T"; then
	sudo smartctl -A /dev/sda | grep "^241"
	sudo smartctl -A /dev/sda | awk '/^241/ { print "/dev/sda TBW: "$10 * 1.0e-3, "TB" }'
fi

if sudo smartctl -a /dev/sdb | grep "^Device Model:" | grep "KINGSTON SA400S37480G"; then
	sudo smartctl -A /dev/sdb | grep "^241"
	sudo smartctl -A /dev/sdb | awk '/^241/ { print "/dev/sdb TBW: "$10 * 1.0e-3, "TB" }'
fi

if sudo smartctl -a /dev/nvme0n1 | grep "^Model Number:" | grep "Samsung SSD 970 EVO Plus"; then
	#sudo smartctl -a /dev/nvme0n1 | grep "^Model Number:"
	sudo smartctl -a /dev/nvme0n1 | grep "Data Units Written"
fi

