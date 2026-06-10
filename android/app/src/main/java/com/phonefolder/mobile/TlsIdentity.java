package com.phonefolder.mobile;

import android.content.Context;

import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.asn1.x509.BasicConstraints;
import org.bouncycastle.asn1.x509.ExtendedKeyUsage;
import org.bouncycastle.asn1.x509.Extension;
import org.bouncycastle.asn1.x509.KeyPurposeId;
import org.bouncycastle.asn1.x509.KeyUsage;
import org.bouncycastle.cert.X509v3CertificateBuilder;
import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter;
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder;
import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.bouncycastle.operator.ContentSigner;
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.math.BigInteger;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.security.cert.Certificate;
import java.security.cert.X509Certificate;
import java.security.spec.ECGenParameterSpec;
import java.util.Date;

import javax.net.ssl.KeyManagerFactory;
import javax.net.ssl.SSLContext;

final class TlsIdentity {
    private static final String KEY_ALIAS = "phonefolder";
    private static final String FILE_NAME = "phonefolder-identity.p12";
    private static final char[] STORE_PASSWORD = "PhoneFolderLocalIdentity".toCharArray();

    private final SSLContext sslContext;
    private final String fingerprint;

    private TlsIdentity(SSLContext sslContext, String fingerprint) {
        this.sslContext = sslContext;
        this.fingerprint = fingerprint;
    }

    static TlsIdentity loadOrCreate(Context context) throws Exception {
        File identityFile = new File(context.getFilesDir(), FILE_NAME);
        KeyStore keyStore = KeyStore.getInstance("PKCS12");

        if (identityFile.isFile()) {
            try (FileInputStream input = new FileInputStream(identityFile)) {
                keyStore.load(input, STORE_PASSWORD);
            }
        } else {
            keyStore.load(null, STORE_PASSWORD);
            createIdentity(keyStore);

            File temporaryFile = new File(context.getFilesDir(), FILE_NAME + ".tmp");
            try (FileOutputStream output = new FileOutputStream(temporaryFile)) {
                keyStore.store(output, STORE_PASSWORD);
                output.getFD().sync();
            }
            if (!temporaryFile.renameTo(identityFile)) {
                throw new IllegalStateException("Could not save the PhoneFolder TLS identity.");
            }
        }

        KeyManagerFactory keyManagers = KeyManagerFactory.getInstance(
                KeyManagerFactory.getDefaultAlgorithm());
        keyManagers.init(keyStore, STORE_PASSWORD);

        SSLContext sslContext = SSLContext.getInstance("TLS");
        sslContext.init(keyManagers.getKeyManagers(), null, new SecureRandom());

        Certificate certificate = keyStore.getCertificate(KEY_ALIAS);
        if (certificate == null) {
            throw new IllegalStateException("The PhoneFolder TLS certificate is unavailable.");
        }

        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        return new TlsIdentity(
                sslContext,
                formatFingerprint(digest.digest(certificate.getEncoded())));
    }

    private static void createIdentity(KeyStore keyStore) throws Exception {
        SecureRandom random = new SecureRandom();
        KeyPairGenerator generator = KeyPairGenerator.getInstance("EC");
        generator.initialize(new ECGenParameterSpec("secp256r1"), random);
        KeyPair keyPair = generator.generateKeyPair();

        long now = System.currentTimeMillis();
        X500Name subject = new X500Name("CN=PhoneFolder Android");
        X509v3CertificateBuilder certificateBuilder = new JcaX509v3CertificateBuilder(
                subject,
                new BigInteger(63, random).add(BigInteger.ONE),
                new Date(now - 24L * 60 * 60 * 1000),
                new Date(now + 20L * 365 * 24 * 60 * 60 * 1000),
                subject,
                keyPair.getPublic());
        certificateBuilder.addExtension(
                Extension.basicConstraints,
                true,
                new BasicConstraints(false));
        certificateBuilder.addExtension(
                Extension.keyUsage,
                true,
                new KeyUsage(KeyUsage.digitalSignature));
        certificateBuilder.addExtension(
                Extension.extendedKeyUsage,
                false,
                new ExtendedKeyUsage(KeyPurposeId.id_kp_serverAuth));

        BouncyCastleProvider provider = new BouncyCastleProvider();
        ContentSigner signer = new JcaContentSignerBuilder("SHA256withECDSA")
                .setProvider(provider)
                .build(keyPair.getPrivate());
        X509Certificate certificate = new JcaX509CertificateConverter()
                .setProvider(provider)
                .getCertificate(certificateBuilder.build(signer));
        certificate.checkValidity(new Date());
        certificate.verify(keyPair.getPublic());

        keyStore.setKeyEntry(
                KEY_ALIAS,
                keyPair.getPrivate(),
                STORE_PASSWORD,
                new Certificate[]{certificate});
    }

    SSLContext sslContext() {
        return sslContext;
    }

    String fingerprint() {
        return fingerprint;
    }

    private static String formatFingerprint(byte[] value) {
        StringBuilder output = new StringBuilder(value.length * 3 - 1);
        for (int index = 0; index < value.length; index++) {
            if (index > 0) {
                output.append(':');
            }
            output.append(String.format("%02X", value[index] & 0xff));
        }
        return output.toString();
    }
}
