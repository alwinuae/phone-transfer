package com.phonefolder.mobile;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Build;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Locale;
import java.util.Set;

final class NetworkUtils {
    private NetworkUtils() {
    }

    static String deviceName() {
        String manufacturer = capitalize(Build.MANUFACTURER);
        String model = Build.MODEL == null ? "Android" : Build.MODEL.trim();
        if (model.toLowerCase(Locale.ROOT).startsWith(manufacturer.toLowerCase(Locale.ROOT))) {
            return model;
        }
        return (manufacturer + " " + model).trim();
    }

    static String localIpv4Address(Context context) {
        Set<String> addresses = localIpv4Addresses(context);
        return addresses.isEmpty() ? "" : addresses.iterator().next();
    }

    static Set<InetAddress> discoveryBroadcastAddresses(Context context) {
        Set<InetAddress> broadcasts = new LinkedHashSet<>();
        try {
            broadcasts.add(InetAddress.getByName("255.255.255.255"));
            for (String address : localIpv4Addresses(context)) {
                byte[] bytes = InetAddress.getByName(address).getAddress();
                bytes[3] = (byte) 255;
                broadcasts.add(InetAddress.getByAddress(bytes));
            }
        } catch (Exception ignored) {
        }
        return broadcasts;
    }

    private static Set<String> localIpv4Addresses(Context context) {
        Set<String> addresses = new LinkedHashSet<>();
        ConnectivityManager connectivity =
                (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
        if (connectivity != null) {
            try {
                for (Network network : connectivity.getAllNetworks()) {
                    NetworkCapabilities capabilities = connectivity.getNetworkCapabilities(network);
                    if (capabilities == null
                            || (!capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
                            && !capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))) {
                        continue;
                    }

                    LinkProperties properties = connectivity.getLinkProperties(network);
                    if (properties == null) {
                        continue;
                    }
                    for (LinkAddress linkAddress : properties.getLinkAddresses()) {
                        addAddress(addresses, linkAddress.getAddress());
                    }
                }
            } catch (Exception ignored) {
            }
        }

        if (!addresses.isEmpty()) {
            return addresses;
        }

        try {
            for (NetworkInterface network : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                String name = network.getName().toLowerCase(Locale.ROOT);
                if (!network.isUp()
                        || network.isLoopback()
                        || name.startsWith("tun")
                        || name.startsWith("rmnet")
                        || name.startsWith("ccmni")) {
                    continue;
                }
                if (!(name.startsWith("wlan")
                        || name.startsWith("wifi")
                        || name.startsWith("eth")
                        || name.startsWith("ap"))) {
                    continue;
                }
                for (InetAddress address : Collections.list(network.getInetAddresses())) {
                    addAddress(addresses, address);
                }
            }
        } catch (Exception ignored) {
        }
        return addresses;
    }

    private static void addAddress(Set<String> addresses, InetAddress address) {
        if (address instanceof Inet4Address
                && !address.isLoopbackAddress()
                && !address.isLinkLocalAddress()) {
            addresses.add(address.getHostAddress());
        }
    }

    private static String capitalize(String value) {
        if (value == null || value.isEmpty()) {
            return "";
        }
        return Character.toUpperCase(value.charAt(0)) + value.substring(1);
    }
}
